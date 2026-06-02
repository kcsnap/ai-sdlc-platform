# AI SDLC Platform Master TODO

This file is the master checklist for building the full AI SDLC platform, the reusable React/C# application template, and the first demo app.

The platform repo is **not** an application repo. It is the reusable orchestration platform that will manage future application repositories.

## Repository model

```text
ai-sdlc-platform
  Reusable AI SDLC orchestration platform.

ai-sdlc-react-dotnet-template
  Future template repo for React + C# application projects.

launchcart
  Future example application repo created from the template.
```

## Working rules

- [x] Keep each implementation slice small.
- [x] Create one branch per slice.
- [ ] Use PRs for every meaningful change.
- [x] Ensure `dotnet build` passes before moving on.
- [x] Ensure `dotnet test` passes before moving on.
- [ ] Add/update tests with every code change.
- [ ] Do not implement live external integrations until interfaces and tests are stable.
- [x] Prefer deterministic code before AI/model behaviour.
- [x] Avoid introducing Next.js; v1 application target stack is React + C#.
- [x] Keep the platform repo separate from application repos.
- [ ] Keep provider-specific code behind abstractions.
- [ ] Keep secrets out of prompts, tests, source files and logs.

---

# 0. Current status

- [x] Repository created: `kcsnap/ai-sdlc-platform`
- [x] Initial scaffold pushed to `main`
- [x] React/C# confirmed as the v1 application target stack
- [x] ChatGPT GitHub connector enabled for the repo
- [x] PR #1 opened for shared domain models
- [x] Duplicate shared model definitions removed from PR branch
- [x] Invalid placeholder test namespaces fixed
- [x] Missing `using Xunit;` imports added to test files
- [x] PR #1 merged to `main`
- [x] `TODO.md` added
- [ ] Expand this backlog as new implementation details emerge

---

# 1. Platform foundation

## 1.1 Shared domain models

Purpose: create the shared vocabulary used by the orchestrator, agents, GitHub integration, audit logging, risk engine and future workflows.

- [x] `AgentContext`
- [x] `AgentResult`
- [x] `AuditEvent`
- [x] `RiskLevel`
- [x] `WorkflowRunStatus`
- [x] `RiskDecision`
- [x] `GitHubIssueReference`
- [x] `GitHubPullRequestReference`
- [x] `ArtefactReference`
- [x] `WorkflowRun`
- [x] Shared model tests added
- [x] PR merged

## 1.2 Solution hygiene

- [x] Confirm every project is included in `AiSdlc.sln`
- [x] Remove any redundant placeholder code once real tests exist
- [x] Add solution-level `Directory.Build.props`
- [x] Add nullable/reference/analyser settings consistently
- [x] Add common code style/analyser packages if required
- [x] Add architecture decision record for initial platform structure
- [x] Add contribution guide
- [x] Add local developer setup guide

## Codex CLI prompt

```text
Read TODO.md. Work on Platform foundation / Solution hygiene.
Make the solution structure cleaner without changing behaviour.
Add Directory.Build.props if useful, confirm all projects are included in the solution, remove redundant placeholder code only where real tests exist, and ensure dotnet build and dotnet test pass.
```

---

# 2. Risk and policy engine

## Goal

Create the first deterministic risk rules engine. This should not use AI yet. It should provide repeatable baseline risk scoring based on change metadata.

## Suggested files

```text
src/AiSdlc.Risk/
  IRiskRulesEngine.cs
  RiskRulesEngine.cs
  RiskAssessmentRequest.cs
  RiskAssessmentResult.cs
  RiskSignal.cs
  RiskRule.cs

tests/AiSdlc.Risk.Tests/
  RiskRulesEngineTests.cs
```

## Tasks

- [x] Accept changed file paths
- [x] Accept affected areas
- [x] Accept quality gate results
- [x] Accept whether Terraform changed
- [x] Accept whether database migrations changed
- [x] Accept whether GitHub Actions workflows changed
- [x] Accept whether auth/payment/security/privacy areas changed
- [x] Accept whether personal data handling changed
- [x] Accept whether secrets/Key Vault changed
- [x] Return `RiskLevel`
- [x] Return `RiskDecision`
- [x] Return rationale
- [x] Return triggered rules/signals
- [ ] Support configurable thresholds later
- [x] Add tests for low, medium and high risk scenarios
- [x] Add tests for failed mandatory quality gates
- [x] Add tests for unknown/ambiguous risk

## Initial deterministic rules

- [x] Docs-only changes default to low risk
- [x] Tests-only changes default to low risk
- [x] Simple frontend/content changes default to low risk
- [x] API changes default to medium risk
- [x] Database migration changes default to medium risk
- [x] Terraform changes default to medium risk
- [x] GitHub Actions workflow changes default to medium risk
- [x] Authentication/authorisation changes default to high risk
- [x] Payment/checkout changes default to high risk
- [x] Personal data handling changes default to high risk
- [x] Secrets/Key Vault changes default to high risk
- [x] Failed mandatory quality gates prevent autonomous continuation
- [x] Unknown/ambiguous signal prevents autonomous continuation

## Codex CLI prompt

```text
Read TODO.md.
Start Phase 2: Risk rules engine foundation.
Create the first deterministic risk rules engine in AiSdlc.Risk.
Do not use AI/model calls yet.
Use simple C# classes, records and enums.
Depend on AiSdlc.Shared where appropriate.
Add an IRiskRulesEngine interface, RiskRulesEngine implementation, request/result models, triggered risk signals and xUnit tests for low, medium, high and blocked scenarios.
Ensure dotnet build and dotnet test pass.
```

---

# 3. Audit and observability foundation

## 3.1 Audit service foundation

Goal: create the audit service abstraction and an in-memory implementation for early testing.

Suggested files:

```text
src/AiSdlc.Audit/
  IAuditService.cs
  InMemoryAuditService.cs
  AuditWriteResult.cs

tests/AiSdlc.Audit.Tests/
  InMemoryAuditServiceTests.cs
```

Tasks:

- [x] Confirm `IAuditService` exists and compiles
- [x] Create `InMemoryAuditService`
- [x] Add ability to write an `AuditEvent`
- [x] Add ability to retrieve events by `RunId`
- [x] Add tests
- [x] Defer Azure Storage/Cosmos/Blob implementations

## 3.2 Secure audit storage later

- [ ] Decide audit store: Azure Table/Cosmos/SQL for structured events
- [ ] Decide prompt/response store: Azure Blob Storage recommended
- [ ] Add audit event hashing/content integrity strategy
- [ ] Add run-level audit summary generation
- [ ] Add retention policy
- [ ] Add access control model
- [ ] Add tamper-resistance approach

## 3.3 Observability

- [x] Add Application Insights integration
- [ ] Add Log Analytics workspace integration
- [ ] Add correlation IDs using `RunId`
- [ ] Add structured logging conventions
- [ ] Add workflow metrics
- [ ] Add agent execution metrics
- [ ] Add risk assessment metrics
- [ ] Add deployment outcome metrics
- [x] Add dashboards (Blazor Server `AiSdlc.Dashboard` — see section 3.4)

## 3.4 Live activity dashboard (`AiSdlc.Dashboard`)

Goal: view-only Blazor Server app that tails the `AuditEvents` Azure Table and renders applications,
issues, run detail (with workflow diagram) and a live activity feed. Reads from the same storage
the orchestrator writes to via `DefaultAzureCredential`.

### Shipped

- [x] `AiSdlc.Dashboard` project scaffolded; added to `AiSdlc.sln` and CI
- [x] `AuditFeedService` background poller — tails `AuditEvents`, 24h backfill, 2s tick
- [x] `DashboardEventBus` — in-memory ring buffer + pub/sub fan-out to pages
- [x] `IAuditService.GetSinceAsync(since, max, ct)` added to both implementations
- [x] `/applications` (default landing page) — one row per repository, health chip, status breakdown, latest run, GitHub link; ready for additional repos
- [x] `/issues` (or `/issues?repo=...`) — paginated 10/page, status + retry + GitHub-state chips, clickable issue title (linked to GitHub), clear-filter when scoped, free-text filter
- [x] `/runs/{runId}` — left-to-right workflow diagram above table; 11 stages, parallel agents stacked top-to-bottom; 4-state visualisation (Not started / In progress / Complete / Failed) + Skipped (dashed amber + ⊘ for agents that would have run if workflow hadn't stopped upstream); chronological table ordering; clickable issue header
- [x] `/activity` — newest-first live event feed; clickable rows expand to show prompt/response from blob; ⚠ icon + red bar + collapsible stack trace on failed agent rows; retry attempts folded into the drill-down via `FeedGrouper`
- [x] Top-nav: `Applications | Issues | Live activity`
- [x] GitHub linking: per-row `↗` to issue (or PR), `💬` linking directly to the orchestrator-posted comment when audit has its URL
- [x] Issue title + state sourced from webhook audit `References`; GitHub REST API fallback (`GitHubIssueLookup`) for runs whose audit data predates the webhook instrumentation
- [x] `RunStatus.Stopped` derived from new `Workflow`-actor audit events written by orchestrator's `RecordWorkflowExitAsync` activity (8 early-exit points instrumented)
- [x] 53 dashboard unit tests covering grouping, projection, error detection, retry counts, run summarisation, status precedence (incl. Stopped), workflow-state derivation (incl. Skipped), application aggregation, issue-title extraction, GitHub-state formatting
- [x] `tools/SeedAudit` console helper for seeding Azurite with sample audit data for local demos

### Local run

Requires `GitHubPat` env var (for the GitHub API fallback) and Azure CLI login (for `DefaultAzureCredential` to read the audit storage account). Defaults in `appsettings.Development.json` point at `staisdlcdev81c0`.

```powershell
$env:GitHubPat = (Get-Content src/AiSdlc.Orchestrator/local.settings.json | ConvertFrom-Json).Values.GitHubPat
cd src/AiSdlc.Dashboard; dotnet run
# → http://localhost:5080
```

### Next steps

- [ ] **Deploy dashboard to Azure** (App Service or Container App) with managed-identity access to the audit storage account — Terraform module + CI deploy step
- [ ] **Authentication** — Easy Auth (Azure AD) so it can be exposed beyond `localhost`; until then it stays a local-only tool
- [ ] **Stopped health bucket on the Applications page** — currently a repo with only Stopped runs shows as "Idle" (misleading); add a "Halted" health label that wins over Running for the latest-status precedence
- [ ] **Date-range filter** on `/issues` (e.g. last 24h / 7d / 30d / all)
- [ ] **Push updates via SSE or SignalR** instead of 2s polling — would tighten the lag from ~2s to <100ms and reduce table-storage requests
- [ ] **Skipped reason in tooltip** — currently says "Skipped (workflow stopped upstream)"; could include the specific stop reason (e.g. "Code implementer produced no file changes") by looking up the latest Workflow Stopped event's `Summary`
- [ ] **Per-run "RunStarted" lifecycle event** so the Activity page can show a dedicated "Run started" row instead of inferring from the first agent's Started event
- [ ] **Search by issue title across all runs** (currently filter only matches the visible page's events)
- [ ] **Cost/token visualisation** once the orchestrator writes token-usage to audit (see section 6.2 model-provider observability)

## Codex CLI prompt

```text
Read TODO.md.
Create the first audit service implementation.
Keep it local/in-memory only for now.
Use the existing AuditEvent model from AiSdlc.Shared.
Add or update IAuditService if needed.
Add InMemoryAuditService and tests for writing/retrieving events by RunId.
Do not add Azure Storage, Cosmos or Blob yet.
Ensure dotnet build and dotnet test pass.
```

---

# 4. GitHub integration

## 4.1 GitHub service contracts

Goal: create interfaces and request/response models for future GitHub operations without calling the real GitHub API yet.

Suggested files:

```text
src/AiSdlc.GitHub/
  IGitHubService.cs
  IssueDetails.cs
  IssueComment.cs
  PullRequestDetails.cs
  ChangedFile.cs
  CheckRunResult.cs
  CreatePullRequestRequest.cs
```

Tasks:

- [x] Define `IGitHubService`
- [x] Add methods for reading issues/comments
- [x] Add methods for writing issue/PR comments
- [x] Add methods for labels
- [x] Add methods for branch/PR creation
- [x] Add methods for changed files/check results
- [x] Add compile/tests
- [ ] Do not implement live GitHub API calls yet

## 4.2 Real GitHub implementation later

- [ ] Choose GitHub App vs PAT for development
- [ ] Implement webhook signature validation
- [ ] Implement issue opened handler
- [ ] Implement issue comment handler
- [ ] Implement PR opened/synchronised handler
- [ ] Implement check-run/status reader
- [ ] Implement labels updater
- [ ] Implement PR comment writer
- [ ] Implement branch creation
- [ ] Implement file commit support where needed
- [ ] Implement `/approve-brief` detection
- [ ] Implement human review command detection
- [ ] Implement idempotency for repeated webhook events
- [ ] Add integration tests using mocked GitHub client

## 4.3 GitHub repo conventions

- [ ] Add AI SDLC labels creation script
- [ ] Add issue template to template repos
- [ ] Add PR template to template repos
- [ ] Add CODEOWNERS guidance
- [x] Add branch naming conventions (`.github/rulesets/branch-naming.json` + `.github/workflows/branch-policy.yml`)
- [ ] Add AI-generated PR conventions

## Codex CLI prompt

```text
Read TODO.md.
Create GitHub integration contracts only. Do not call the live GitHub API yet.
Add IGitHubService and simple serialisable request/response models for issues, comments, pull requests, changed files and check results.
Use async method signatures and CancellationToken.
Add tests where useful.
Ensure dotnet build and dotnet test pass.
```

---

# 5. Agent runtime and personas

## 5.1 Agent runtime foundation

Suggested files:

```text
src/AiSdlc.Agents/
  IAgent.cs
  IAgentRunner.cs
  AgentRunner.cs
  AgentExecutionRequest.cs
  AgentExecutionResult.cs
  Personas/
    ProductStrategistAgent.cs
    ProductOwnerAgent.cs
    BusinessAnalystAgent.cs
```

Tasks:

- [x] Define `IAgent`
- [x] Define `IAgentRunner`
- [x] Add `AgentRunner`
- [x] Add stub persona classes
- [x] Each stub should return an `AgentResult`
- [x] No real model calls yet
- [x] Add tests for successful execution
- [x] Add tests for unknown agent handling

## 5.2 Full persona list

- [x] Product Strategist (name constant defined)
- [x] Product Owner (name constant defined)
- [x] Business Analyst (agent implemented)
- [x] UX / Accessibility Reviewer (agent implemented)
- [x] Content / SEO Reviewer (agent implemented)
- [x] Data / Analytics Reviewer (agent implemented)
- [x] Compliance / Legal Reviewer (agent implemented)
- [x] Architect (agent implemented)
- [x] Coder (name constant defined)
- [x] QA / Test Engineer (agent implemented)
- [x] Senior Coder (agent implemented)
- [x] Security & Privacy Reviewer (agent implemented)
- [x] DevOps / Platform Engineer (agent implemented)
- [x] Risk Assessor (agent implemented)
- [x] Release Manager (agent implemented)

## 5.3 Persona prompt templates

Suggested files:

```text
src/AiSdlc.Agents/Prompts/
  product-strategist.md
  product-owner.md
  business-analyst.md
  ux-accessibility-reviewer.md
  content-seo-reviewer.md
  data-analytics-reviewer.md
  compliance-legal-reviewer.md
  architect.md
  coder.md
  qa-test-engineer.md
  senior-coder.md
  security-privacy-reviewer.md
  devops-platform-engineer.md
  risk-assessor.md
  release-manager.md
```

Tasks:

- [ ] Define persona responsibilities in prompts
- [ ] Define required input artefacts for each persona
- [ ] Define required output schema for each persona
- [ ] Define when each persona is mandatory vs conditional
- [ ] Add tests for prompt template loading
- [ ] Add output validation

## Codex CLI prompt

```text
Read TODO.md.
Create the first agent runtime foundation.
Add IAgent, IAgentRunner and AgentRunner.
Add stub agents for ProductStrategistAgent, ProductOwnerAgent and BusinessAnalystAgent.
Stubs should return deterministic AgentResult objects.
Do not call real AI/model providers yet.
Add unit tests for successful agent execution and unknown agent handling.
Ensure dotnet build and dotnet test pass.
```

---

# 6. Model providers

## 6.1 Provider contracts

Suggested files:

```text
src/AiSdlc.ModelProviders/
  IModelProvider.cs
  ModelRequest.cs
  ModelResponse.cs
  ModelProviderOptions.cs
```

Tasks:

- [x] Add provider abstraction
- [x] Add request/response models
- [x] Add stub/fake provider for tests
- [x] No live provider calls yet
- [x] Add tests

## 6.2 Azure OpenAI provider later

- [ ] Add Azure OpenAI provider implementation
- [ ] Add Key Vault/config integration
- [ ] Add model selection configuration
- [ ] Add retry/backoff policy
- [ ] Add token usage recording
- [ ] Add prompt/response audit hooks
- [ ] Add tests with mocked provider

## 6.3 Azure AI Foundry provider later

- [ ] Add AI Foundry provider abstraction/adapter
- [ ] Decide how Foundry agents map to platform personas
- [ ] Add provider configuration
- [ ] Add tests with mocked provider

## 6.4 GitHub Copilot/coding provider later

- [ ] Define Coder provider abstraction
- [ ] Decide whether GitHub Copilot coding agent is invoked via GitHub-native workflows, issue assignment or another integration
- [ ] Add handoff format for coding tasks
- [ ] Add PR monitoring logic
- [ ] Add fallback flow if coding agent cannot complete task

## Codex CLI prompt

```text
Read TODO.md.
Create model provider contracts only.
Add IModelProvider, ModelRequest and ModelResponse models.
Add a simple fake/stub provider for tests.
Do not call Azure OpenAI, OpenAI, AI Foundry or GitHub Copilot yet.
Add unit tests.
Ensure dotnet build and dotnet test pass.
```

---

# 7. Durable orchestration

## 7.1 Orchestrator skeleton

Suggested files:

```text
src/AiSdlc.Orchestrator/
  Functions/
    AiSdlcWorkflowOrchestrator.cs
    GitHubIssueWebhookFunction.cs
    AgentActivityFunctions.cs
```

Tasks:

- [x] Add orchestrator function skeleton
- [x] Add HTTP-triggered webhook placeholder
- [x] Add activity function placeholders
- [x] No real GitHub webhook validation yet
- [x] No real agent execution yet unless already available through interfaces
- [x] Build/test passes

## 7.2 Workflow states

- [x] Started
- [x] AwaitingClarification
- [x] BriefReady
- [x] AwaitingBriefApproval
- [x] BriefApproved
- [x] Analysing
- [x] Implementing
- [x] PullRequestOpen
- [x] Reviewing
- [x] RiskAssessing
- [x] AwaitingHumanReview
- [x] ReadyToRelease
- [x] Deploying
- [x] Released
- [x] Stopped
- [x] Failed

## 7.3 External events

- [x] Issue created
- [x] Issue comment added
- [x] `/approve-brief`
- [x] PR opened (`PullRequestReady` event raised from `HandlePullRequestEventAsync`)
- [ ] PR updated (re-evaluate gates on new push)
- [ ] GitHub Actions completed
- [x] Human review approved (`/approve-merge`, 14-day timeout)
- [x] Human review rejected
- [ ] Deployment completed (`DeploymentCompleted` event — future: launchcart CD posts back)
- [ ] Post-deployment checks completed

## Codex CLI prompt

```text
Read TODO.md.
Create the first Azure Durable Functions orchestrator skeleton.
Add AiSdlcWorkflowOrchestrator, a simple HTTP-triggered GitHubIssueWebhookFunction placeholder and activity function placeholders for the first few agents.
Use Microsoft.Azure.Functions.Worker isolated worker style.
Do not implement real GitHub webhook security yet.
Do not implement real model calls yet.
Ensure dotnet build and dotnet test pass.
```

---

# 8. Security and identity

## 8.1 Platform identity

- [ ] Create managed identity for Azure Functions
- [ ] Configure Key Vault access for managed identity
- [ ] Ensure no secrets are stored in source
- [ ] Ensure local development uses user secrets or environment variables only
- [ ] Add least-privilege permission model

## 8.2 GitHub Actions to Azure

- [x] Configure OIDC federation in `ci.yml` (`permissions: id-token: write`)
- [x] Remove need for long-lived Azure credentials (using `azure/login@v2` with OIDC)
- [x] Create service principal and federated credentials (one-time, see section 9.2)
- [ ] Define deployment permissions per environment
- [ ] Add GitHub environment protections where available/free

## 8.3 Secret and PII redaction

- [x] Create redaction abstraction
- [x] Detect common secret patterns
- [x] Detect common PII patterns
- [x] Redact before storing full prompts/responses
- [ ] Store redaction metadata in audit events
- [x] Add tests for redaction

## 8.4 Compliance/security checks

- [ ] GDPR review checklist
- [ ] OWASP Top 10 review checklist
- [ ] Security/privacy reviewer output schema
- [ ] Compliance/legal reviewer output schema
- [ ] Human escalation rules for high-risk findings

---

# 9. Terraform and Azure infrastructure

## 9.1 Platform infrastructure

Suggested Terraform structure:

```text
infra/terraform/
  modules/
    function-app/
    key-vault/
    storage-account/
    application-insights/
    log-analytics/
  environments/
    dev/
    test/
    staging/
    production/
```

Tasks:

- [x] Create Terraform backend strategy
- [x] Create Azure Resource Group module/usage
- [x] Create Storage Account for Functions/Durable state (host + audit)
- [x] Create Azure Function App (Linux Consumption Y1)
- [x] Create Application Insights
- [x] Create Log Analytics Workspace
- [x] Create Key Vault with managed identity secret reader
- [x] Create user-assigned managed identity
- [x] Wire `AZURE_CLIENT_ID` from managed identity (no longer left blank)
- [x] Add `webhook_url` Terraform output
- [x] Add environment-specific variables
- [ ] Add Terraform validate/plan workflow (GitHub Actions)
- [x] Run `terraform apply` for the `dev` environment (14/14 resources live in `rg-aisdlc-dev`, North Europe)

## 9.2 Production deployment — one-time setup

Steps required before the Function App is live. See also section 8.2. **All complete as of 2026-05-12.**

- [x] **Create Azure service principal for GitHub Actions (OIDC)** — `sp-aisdlc-github` with federated credential for `repo:kcsnap/ai-sdlc-platform:ref:refs/heads/main`
- [x] **Run `terraform apply`** — 14/14 resources live in `rg-aisdlc-dev` (North Europe)
- [x] **Load secrets into Key Vault** — `kv-aisdlc-81c0` populated with `AnthropicApiKey`, `GitHubPat`, `GitHubWebhookSecret`
- [x] **Add GitHub Actions secrets** to `kcsnap/ai-sdlc-platform` — `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_FUNCTION_APP_NAME` (`func-aisdlc-dev-81c0`)
- [x] **Create a `dev` GitHub environment** — exists; deploy job targets `environment: dev`
- [x] **Update launchcart webhook to the permanent Azure URL** — hook 621737166 now points at `https://func-aisdlc-dev-81c0.azurewebsites.net/api/github/webhook`
- [x] **Add `pull_request` to launchcart webhook events** — events now `[issues, issue_comment, pull_request]`
- [x] **Trigger first deploy** — CI/CD deploys on every push to `main`

## 9.2 Application infrastructure modules later

> **Note:** for v1, user-app infrastructure is provisioned centrally by Yorrixx Hosting per [ADR-0002](docs/adr/0002-app-template-stack.md) — user-app repos contain no `infra/`. The list below is the longer-term wishlist of reusable modules the platform may publish for repos that opt out of central provisioning.

- [ ] Web App (F1/B1) module for React frontend + .NET minimal API (per ADR-0002)
- [ ] Cosmos DB serverless container module (per ADR-0002)
- [ ] Static Web App module (post-v1 alternative for static-only frontends)
- [ ] App Service module (post-v1 alternative for larger workloads)
- [ ] Azure SQL module (post-v1)
- [ ] PostgreSQL module (post-v1)
- [ ] Key Vault module
- [ ] Application Insights module
- [ ] Log Analytics module
- [ ] Storage Account module
- [ ] Optional Front Door/CDN later

---

# 10. Quality gates and CI/CD

## 10.1 Platform CI

Suggested file:

```text
.github/workflows/ci.yml
```

Tasks:

- [x] Trigger on PRs to `main`
- [x] Trigger on pushes to `main`
- [x] Setup .NET 8 SDK
- [x] Run `dotnet restore`
- [x] Run `dotnet build --configuration Release --no-restore`
- [x] Run `dotnet test --configuration Release --no-build`
- [x] Publish and upload artifact on push to `main`
- [x] Deploy job: Azure login (OIDC) + `azure/functions-action` on push to `main`

## 10.2 Platform quality gates

- [ ] Unit tests required
- [ ] Lint/format check
- [ ] Secret scanning where available/free
- [ ] Dependency scanning where available/free
- [ ] SAST/code scanning where available/free
- [ ] Terraform validation
- [ ] Terraform plan
- [ ] IaC scanning where available/free

## 10.3 App repo quality gates later

- [ ] React unit/component tests
- [ ] C# API unit tests
- [ ] API integration tests
- [ ] E2E tests
- [ ] Accessibility tests
- [ ] Lighthouse/performance checks
- [ ] Coverage warning on drop
- [ ] Security checks
- [ ] Terraform plan/apply gates

## Codex CLI prompt

```text
Read TODO.md.
Add a GitHub Actions CI workflow for .NET.
Create .github/workflows/ci.yml.
Trigger on PRs to main and pushes to main.
Use .NET 8 SDK.
Run dotnet restore, dotnet build --configuration Release --no-restore, and dotnet test --configuration Release --no-build.
Keep the workflow simple.
```

---

# 11. Repo onboarding and knowledge indexing

## Goal

Allow existing application repositories to be inspected and mapped into the AI SDLC model.

Tasks:

- [ ] Define repo index model
- [ ] Scan repo structure
- [ ] Detect frontend framework
- [ ] Detect backend framework
- [ ] Detect routes/pages
- [ ] Detect APIs/controllers/endpoints
- [ ] Detect services/domain logic
- [ ] Detect database access/migrations
- [ ] Detect tests
- [ ] Detect Terraform
- [ ] Detect GitHub Actions workflows
- [ ] Detect environment configuration
- [ ] Detect dependencies
- [ ] Detect authentication/authorisation areas
- [ ] Detect personal data handling areas
- [ ] Detect payment/checkout areas
- [ ] Detect high-risk files/directories
- [ ] Generate missing baseline docs during onboarding
- [ ] Store repo index artefacts
- [ ] Add tests using sample repo fixtures

---

# 12. Artefact generation

## Artefacts required per workflow

- [ ] Refined brief
- [ ] Product strategy review
- [ ] User stories
- [ ] Acceptance criteria
- [ ] Functional impact analysis
- [ ] Technical analysis
- [ ] Architecture notes
- [ ] ADRs when needed
- [ ] UX/accessibility review
- [ ] Content/SEO review
- [ ] Data/analytics review
- [ ] Compliance/legal review
- [ ] Test plan
- [ ] Security/privacy review
- [ ] DevOps review
- [ ] Risk assessment
- [ ] Release notes
- [ ] Rollback plan
- [ ] Final audit summary

## Tasks

- [ ] Define artefact storage abstraction
- [ ] Define markdown artefact renderer
- [ ] Define JSON artefact renderer
- [ ] Persist artefacts to storage
- [ ] Optionally commit artefacts to PR branch
- [ ] Summarise artefacts in GitHub comments
- [ ] Add tests

---

# 13. Human review and approval routing

## Tasks

- [ ] Define reviewer routing rules
- [ ] Route product value issues to Product Strategist
- [ ] Route product ambiguity to Product Owner
- [ ] Route functional issues to Business Analyst
- [ ] Route UX/accessibility issues to UX / Accessibility Reviewer
- [ ] Route content/SEO issues to Content / SEO Reviewer
- [ ] Route analytics issues to Data / Analytics Reviewer
- [ ] Route legal/compliance issues to Compliance / Legal Reviewer
- [ ] Route architecture issues to Architect
- [ ] Route code quality issues to Senior Coder
- [ ] Route testing issues to QA / Test Engineer
- [ ] Route security/privacy issues to Security & Privacy Reviewer
- [ ] Route infra/deployment issues to DevOps / Platform Engineer
- [ ] Route final release decisions to Release Manager
- [ ] Implement approval/rejection commands
- [ ] Implement timeout/reminder behaviour later

---

# 14. Autonomous merge and deployment gates

## Low-risk auto-deploy threshold

A change can auto-merge and auto-deploy only when all are true:

- [ ] Risk is low
- [ ] Product Owner approved refined brief
- [ ] Product Strategist has not raised a blocking value concern
- [ ] All mandatory checks pass
- [ ] No unresolved agent review comments
- [ ] No unresolved human-review requirement
- [ ] Tests generated/updated where relevant
- [ ] GDPR/OWASP checks pass or are not applicable
- [ ] Rollback path documented
- [ ] Release notes generated
- [ ] Post-deployment checks defined

## Implementation tasks

- [x] Implement auto-merge eligibility service
- [x] Read GitHub check results (`GetCheckRunResultsAsync` in `GetPullRequestContextAsync`)
- [x] Read risk assessment result (risk decision routes Phase 2)
- [x] Read human review state (`/approve-merge` raises `HumanReviewApproved`)
- [x] Block auto-deploy on missing/unknown signals (gate evaluation returns `IsEligible=false`)
- [x] Auto-merge LOW risk changes when all 10 gates pass
- [x] Human-review path for MEDIUM risk or gate failures
- [x] Add tests
- [ ] Implement production deployment eligibility service (post-merge CD is launchcart's pipeline)

---

# 15. Deployment, monitoring and rollback

## Deployment tasks

- [ ] Define deployment event model
- [ ] Integrate GitHub Actions deployment results
- [ ] Track environment promotion dev/test/staging/production
- [ ] Add release manager checks
- [ ] Generate release notes
- [ ] Generate rollback plan

## Monitoring tasks

- [ ] Define post-deployment health check model
- [ ] Capture health endpoint result
- [ ] Capture smoke test result
- [ ] Capture error-rate signals later
- [ ] Capture Application Insights signals later

## Hybrid rollback tasks

- [ ] Define safe rollback scenarios
- [ ] Define unsafe rollback scenarios requiring humans
- [ ] Add alert-only behaviour for ambiguous/risky rollback
- [ ] Add audit trail for rollback decisions

---

# 16. Application template repo: `ai-sdlc-react-dotnet-template`

## Goal

Create a reusable starter template for the user-apps the platform generates. **Stack is locked in [ADR-0002](docs/adr/0002-app-template-stack.md)** — refer to it for tech, hosting, naming, RBAC, and provisioning order. Tasks below cover only the files the template repo itself ships.

## Template structure

```text
/
├── .github/
│   ├── workflows/         # ci.yml + deploy.yml (no infra workflows — Yorrixx provisions)
│   ├── ISSUE_TEMPLATE/
│   ├── pull_request_template.md
│   └── CODEOWNERS
├── .ai-sdlc.yml           # user-app stack per ADR-0002
├── docs/
├── src/
│   ├── frontend/          # React 19 + Vite + TypeScript + TanStack Query + Tailwind + shadcn/ui
│   ├── api/               # vanilla ASP.NET Core minimal API (.NET 9) — no Aspire deps
│   └── AppHost/           # .NET Aspire AppHost — local dev only, never deployed
├── tests/                 # Vitest + xUnit; Playwright + Cosmos emulator scaffolded
└── README.md              # documents F1 cold-start UX + dev-loop
```

**No `infra/` directory** — Yorrixx's Hosting module owns all Azure provisioning per ADR-0002.

## Tasks

- [ ] Create `ai-sdlc-react-dotnet-template` repo
- [ ] Add React 19 + Vite + TS frontend (TanStack Query + Tailwind + shadcn/ui)
- [ ] Add ASP.NET Core minimal API on .NET 9 (vanilla — no Aspire deps in API project)
- [ ] Add .NET Aspire AppHost project for local dev (Aspire orchestrates API + Vite + Cosmos emulator)
- [ ] Add Vitest + xUnit test scaffolding; Playwright + Cosmos-emulator integration scaffolded but not CI-gated
- [ ] Add `.github/workflows/ci.yml` — build + test on PR
- [ ] Add `.github/workflows/deploy.yml` — OIDC + zip deploy via `azure/webapps-deploy@v3` to both F1 Web Apps (frontend + API). AppHost excluded
- [ ] Add `.ai-sdlc.yml` reflecting ADR-0002 (backend dotnet 9, frontend react/vite/ts, database cosmos serverless)
- [ ] Add mandatory docs (README documenting F1 5–10s cold start + `*.azurewebsites.net` URL only on free tier)
- [ ] Add issue template
- [ ] Add PR template
- [ ] Add CODEOWNERS example
- [ ] Add local dev guide (`dotnet run --project src/AppHost`; user-secrets for Clerk dev key; `.env.local` for Vite)
- [ ] Add build/test verification

---

# 17. Example app repo: `launchcart`

## Goal

Create the first example app that demonstrates the AI SDLC end to end.

## Target stack

Stack inherited from [ADR-0002](docs/adr/0002-app-template-stack.md) — launchcart is the first user-app, so it gets the same shape as every other user-app:

- [ ] React 19 + Vite + TypeScript + TanStack Query + Tailwind + shadcn/ui (frontend)
- [ ] ASP.NET Core minimal API on .NET 9 (api)
- [ ] Cosmos DB serverless container (data)
- [ ] GitHub Actions ci.yml + deploy.yml (OIDC + zip deploy via `azure/webapps-deploy@v3`)
- [ ] Two F1 Web Apps per user-app (frontend + api on one F1 plan), provisioned centrally by Yorrixx Hosting

## App features

- [ ] Home page
- [ ] Product catalogue
- [ ] Product detail page
- [ ] Purchase enquiry form
- [ ] Admin products page
- [ ] Admin enquiries page
- [ ] Products API
- [ ] Enquiries API
- [ ] Cosmos container schema + seed data
- [ ] Frontend tests (Vitest)
- [ ] API tests (xUnit)
- [ ] E2E tests (Playwright — scaffolded, not CI-gated for v1)
- [ ] Accessibility tests
- [ ] Docs
- [ ] `.ai-sdlc.yml`

## First AI SDLC demo change

- [ ] Open GitHub issue: Add delivery information section to product detail page
- [ ] Product Owner agent asks clarification questions
- [ ] Refined brief generated
- [ ] Product Owner approves with `/approve-brief`
- [ ] Business Analyst maps affected areas
- [ ] Coder creates branch/PR
- [ ] Tests generated/updated
- [ ] Review agents run
- [ ] Risk Assessor marks low risk
- [ ] GitHub Actions pass
- [ ] PR auto-merges
- [ ] App deploys to production
- [ ] Post-deployment checks pass
- [ ] Final audit summary added

---

# 18. End-to-end AI SDLC workflow

## Full workflow tasks

- [ ] GitHub issue opens workflow
- [ ] Product Strategist reviews value
- [ ] Product Owner clarifies request
- [ ] Refined brief generated
- [ ] `/approve-brief` resumes workflow
- [ ] Business Analyst analyses affected areas
- [ ] Architect reviews approach
- [ ] Coder creates implementation branch/PR
- [ ] QA/Test Engineer reviews tests
- [ ] Senior Coder reviews implementation quality
- [ ] Security & Privacy Reviewer performs GDPR/OWASP review
- [ ] DevOps / Platform Engineer reviews infra/pipeline impact
- [ ] Data / Analytics Reviewer reviews tracking/reporting where relevant
- [ ] Compliance / Legal Reviewer reviews legal/compliance where relevant
- [ ] Risk Assessor produces final risk decision
- [ ] Release Manager generates release notes/rollback plan
- [ ] Low-risk eligible PR auto-merges
- [ ] Deployment pipeline promotes to production
- [ ] Post-deployment checks run
- [ ] Audit summary written to issue/PR

---

# 19. Production hardening

## Platform hardening

- [ ] Add authentication/authorisation to platform endpoints
- [ ] Validate GitHub webhook signatures
- [ ] Add idempotency keys for webhooks
- [ ] Add retry policies
- [ ] Add dead-letter/error handling
- [ ] Add rate-limit handling
- [ ] Add concurrency controls
- [ ] Add environment isolation
- [ ] Add backup/restore approach for audit data
- [ ] Add data retention policy
- [ ] Add cost monitoring
- [ ] Add operational runbook
- [ ] Add incident response guide

## Governance hardening

- [ ] Define repo onboarding checklist
- [ ] Define risk policy inheritance model
- [ ] Define central governance templates
- [ ] Define app-level overrides
- [ ] Define minimum documentation requirements
- [ ] Define human review SLAs
- [ ] Define audit review process
- [ ] Define model/provider approval process

---

# 20. Documentation and operating model

## Platform docs

- [x] Architecture overview
- [x] Local development guide
- [ ] Deployment guide
- [ ] GitHub integration guide
- [ ] Agent/persona guide
- [x] Risk model guide
- [ ] Audit/logging guide
- [ ] Security/privacy guide
- [ ] Troubleshooting guide
- [x] Operator runbook

## User docs

- [ ] Product Owner guide
- [ ] How to raise an AI SDLC issue
- [ ] How to approve a refined brief
- [ ] How human review works
- [ ] What low/medium/high risk means
- [ ] How to interpret AI SDLC comments
- [ ] How to onboard an existing repo
- [ ] How to create a new app from the template

---

# 21. Future enhancements

- [ ] Multi-provider model routing
- [ ] Azure AI Foundry hosted agents
- [ ] GitHub Copilot coding-agent integration
- [ ] Repository vector index
- [ ] Semantic search over repo docs/code
- [ ] Advanced policy engine
- [ ] Advanced risk scoring model
- [ ] Cost estimation per workflow
- [ ] Agent performance analytics
- [ ] Human feedback loops
- [ ] Prompt/version management
- [ ] Agent evaluation framework
- [ ] Support for more hosting targets
- [ ] Support for additional frontend/backend stacks later
- [ ] Optional AKS/Container Apps support later

---

# 22. Master completion criteria

The programme is complete when:

- [ ] `ai-sdlc-platform` can receive GitHub issue events
- [ ] Product Owner clarification loop works
- [ ] `/approve-brief` works
- [ ] All core personas can run as deterministic stubs or model-backed agents
- [ ] Artefacts are generated and stored
- [ ] GitHub PRs can be created and updated
- [ ] Build/test/check results can be read
- [ ] Risk is assessed using deterministic rules plus AI judgement later
- [ ] Low-risk changes can auto-merge and auto-deploy
- [ ] Medium/high-risk changes route to humans
- [ ] Full audit trail exists
- [ ] Azure infrastructure is provisioned by Terraform
- [ ] React/C# template repo exists
- [ ] LaunchCart demo app exists
- [ ] First low-risk LaunchCart change completes end to end
- [ ] Documentation and runbooks are sufficient for another app to be onboarded
