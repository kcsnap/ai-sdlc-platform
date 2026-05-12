# Architecture

## Overview

The AI SDLC Platform is an Azure Durable Functions application that orchestrates a pipeline of AI agents across the software development lifecycle. When a GitHub issue is opened in a connected repository, the platform runs a structured multi-agent workflow that analyses, reviews, and — when eligible — autonomously merges and deploys the change.

The platform is separate from the application repositories it manages. Application repos configure it via a `.ai-sdlc.yml` file.

---

## Solution structure

```
ai-sdlc-platform/
  src/
    AiSdlc.Orchestrator/      Azure Durable Functions host — orchestration, webhook entry
    AiSdlc.Agents/            Agent runtime, AgentRunner, and all persona agents
    AiSdlc.Shared/            Shared domain models, redaction, auto-merge eligibility
    AiSdlc.Risk/              Deterministic risk rules engine (no AI)
    AiSdlc.Audit/             Audit service abstraction and Azure Table/Blob implementations
    AiSdlc.GitHub/            GitHub API client and service contracts
    AiSdlc.ModelProviders/    AI model provider abstraction and Anthropic implementation
    AiSdlc.RepoIndex/         Repo knowledge indexer — reads .ai-sdlc.yml and GitHub structure

  tests/
    AiSdlc.*.Tests/           xUnit test projects mirroring src structure
```

---

## Workflow

A GitHub issue event triggers the full pipeline:

```
GitHub issue opened
  └─> GitHubWebhookFunction (HTTP trigger, HMAC-validated)
        └─> AiSdlcWorkflowOrchestrator (Durable orchestrator)
              ├─ 1. RepoIndex           — load .ai-sdlc.yml for repo context
              ├─ 2. ProductStrategist   — strategic value assessment
              ├─ 3. ProductOwner        — clarify requirements (loop until approved)
              │      └─ /approve-brief event resumes the workflow
              ├─ 4. BusinessAnalyst     — functional scope and affected areas
              ├─ 5. Architect           — technical approach and ADR recommendation
              ├─ 6. Parallel reviews    — Security, UX, DevOps, Content, Compliance, Analytics
              ├─ 7. Parallel reviews    — QA/Test Engineer, Senior Coder
              ├─ 8. RiskAssessor        — synthesise all reviews → risk decision
              │      ├─ AUTO_MERGE_ELIGIBLE → continue
              │      ├─ HUMAN_REVIEW_REQUIRED → wait for ApproveRelease event (14-day timeout)
              │      └─ BLOCKED → fail workflow
              └─ 9. ReleaseManager      — release notes, rollback plan, post-deploy checks
```

All agent outputs are posted as GitHub comments and stored via the audit service.

---

## Key components

### AiSdlcWorkflowOrchestrator

The Durable orchestrator function. Manages workflow state, fan-out/fan-in for parallel reviews, and external event waits for human approval. State is persisted by the Durable Task Framework in Azure Storage.

### AgentActivityFunctions

One `[Function]`-decorated activity method per persona. Each activity resolves the agent by name from DI and calls `IAgentRunner.RunAsync`.

### AgentRunner

Resolves an agent by name from the registered `IEnumerable<IAgent>` collection and invokes `IAgent.ExecuteAsync`. Returns a typed `AgentResult`.

### Persona agents

Each persona is a sealed class implementing `IAgent`. The system prompt is embedded as a C# string literal. Agents receive an `AgentExecutionRequest` containing the `AgentContext` (RunId, repo, issue, metadata from prior agents) and return an `AgentResult` with `OutputMarkdown`, `Status`, `Decision`, and `ArtefactsCreated`.

Registered agents:

| Agent | Role |
|-------|------|
| ProductStrategistAgent | Strategic value, alignment with product goals |
| ProductOwnerAgent | Requirements clarification, refined brief |
| BusinessAnalystAgent | Functional scope, affected areas, user stories |
| ArchitectAgent | Technical approach, component impact, ADR |
| SecurityPrivacyReviewerAgent | OWASP Top 10, GDPR, risk signal extraction |
| UxAccessibilityReviewerAgent | WCAG 2.1 AA, user flow impact |
| QaTestEngineerAgent | Test plan, unit/integration/E2E scope |
| SeniorCoderAgent | Implementation spec, numbered steps |
| DevOpsPlatformEngineerAgent | Infrastructure, pipeline, deployment risk |
| ComplianceLegalReviewerAgent | UK/EU GDPR, PSD2, Consumer Rights Act |
| ContentSeoReviewerAgent | SEO, content quality, brand voice |
| DataAnalyticsReviewerAgent | Analytics events, KPI impact, tracking |
| RiskAssessorAgent | Synthesises all reviews → final risk decision |
| ReleaseManagerAgent | Release notes, rollback plan, post-deploy checks |

### RiskRulesEngine

Deterministic (no AI) risk scoring based on changed file patterns. Returns `RiskLevel` (Low/Medium/High/Unknown) and `RiskDecision`. High-risk signals include: auth changes, payment changes, personal data handling, secrets/Key Vault changes. Applied independently of the AI RiskAssessorAgent; both must agree for auto-merge eligibility.

### AutoMergeEligibilityService

Evaluates 10 boolean gates before a PR can be auto-merged:
1. Risk level is Low
2. Risk decision is AUTO_MERGE_ELIGIBLE
3. Brief approved
4. All reviews completed
5. No blocking issues
6. All CI checks pass
7. Test coverage present
8. Rollback documented
9. Release notes generated
10. Post-deployment checks defined

### RegexRedactionService

15 compiled regex rules covering secrets (Anthropic key, OpenAI key, GitHub PAT classic/fine-grained, Azure SAS token, Azure storage key, Bearer token, JWT, private key header, connection string password) and PII (email, UK NINO, UK sort code, credit card number, private IPv4 address). Applied by `AnthropicModelProvider` before sending any prompt to the API.

---

## Infrastructure

The platform runs as an Azure Functions isolated worker (v4, .NET 8) with:

- **Azure Durable Task** — orchestration state and activity dispatch
- **Azure Table Storage** — audit event storage
- **Azure Blob Storage** — prompt/response storage
- **Application Insights** — telemetry (auto-configured when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set)
- **DefaultAzureCredential** — managed identity in Azure, interactive/env credentials locally

---

## Security

- **Webhook validation** — HMAC-SHA256 signature checked against `GitHubWebhookSecret` (skipped when secret is empty for local development)
- **Prompt redaction** — `RegexRedactionService` strips secrets and PII before sending to Anthropic
- **No secrets in source** — all secrets via environment variables / Key Vault
- **Managed identity** — no long-lived storage credentials in deployed environments

---

## Repository configuration (`.ai-sdlc.yml`)

Application repositories include a `.ai-sdlc.yml` to tell the platform about their structure:

```yaml
project:
  name: My App
  description: React + C# e-commerce application
  stack: react-dotnet

agents:
  business_analyst:
    enabled: true
  security_reviewer:
    enabled: true
  # ...

risk:
  auto_merge_threshold: low

areas:
  auth:
    paths: ["src/api/Auth/**", "src/frontend/src/pages/Login*"]
  payments:
    paths: ["src/api/Payments/**", "src/frontend/src/pages/Checkout*"]
```

The `GitHubRepoIndexer` fetches this file via the GitHub Contents API and renders it as a markdown context document that is included in every agent prompt.
