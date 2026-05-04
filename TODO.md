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
- [ ] Add solution-level `Directory.Build.props`
- [ ] Add nullable/reference/analyser settings consistently
- [ ] Add common code style/analyser packages if required
- [ ] Add architecture decision record for initial platform structure
- [x] Add contribution guide
- [ ] Add local developer setup guide

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
- [ ] Defer Azure Storage/Cosmos/Blob implementations

## 3.2 Secure audit storage later

- [ ] Decide audit store: Azure Table/Cosmos/SQL for structured events
- [ ] Decide prompt/response store: Azure Blob Storage recommended
- [ ] Add audit event hashing/content integrity strategy
- [ ] Add run-level audit summary generation
- [ ] Add retention policy
- [ ] Add access control model
- [ ] Add tamper-resistance approach

## 3.3 Observability

- [ ] Add Application Insights integration
- [ ] Add Log Analytics workspace integration
- [ ] Add correlation IDs using `RunId`
- [ ] Add structured logging conventions
- [ ] Add workflow metrics
- [ ] Add agent execution metrics
- [ ] Add risk assessment metrics
- [ ] Add deployment outcome metrics
- [ ] Add dashboards later

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
- [ ] Add branch naming conventions
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

- [ ] Product Strategist
- [ ] Product Owner
- [ ] Business Analyst
- [ ] UX / Accessibility Reviewer
- [ ] Content / SEO Reviewer
- [ ] Data / Analytics Reviewer
- [ ] Compliance / Legal Reviewer
- [ ] Architect
- [ ] Coder
- [ ] QA / Test Engineer
- [ ] Senior Coder
- [ ] Security & Privacy Reviewer
- [ ] DevOps / Platform Engineer
- [ ] Risk Assessor
- [ ] Release Manager

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

- [ ] Started
- [ ] AwaitingClarification
- [ ] BriefReady
- [ ] AwaitingBriefApproval
- [ ] BriefApproved
- [ ] Analysing
- [ ] Implementing
- [ ] PullRequestOpen
- [ ] Reviewing
- [ ] RiskAssessing
- [ ] AwaitingHumanReview
- [ ] ReadyToRelease
- [ ] Deploying
- [ ] Released
- [ ] Stopped
- [ ] Failed

## 7.3 External events

- [ ] Issue created
- [ ] Issue comment added
- [ ] `/approve-brief`
- [ ] PR opened
- [ ] PR updated
- [ ] GitHub Actions completed
- [ ] Human review approved
- [ ] Human review rejected
- [ ] Deployment completed
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

- [ ] Configure GitHub OIDC federation to Azure
- [ ] Remove need for long-lived Azure credentials
- [ ] Define Azure federated credentials per environment
- [ ] Define deployment permissions per environment
- [ ] Add GitHub environment protections where available/free

## 8.3 Secret and PII redaction

- [ ] Create redaction abstraction
- [ ] Detect common secret patterns
- [ ] Detect common PII patterns
- [ ] Redact before storing full prompts/responses
- [ ] Store redaction metadata in audit events
- [ ] Add tests for redaction

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

- [ ] Create Terraform backend strategy
- [ ] Create Azure Resource Group module/usage
- [ ] Create Storage Account for Functions/Durable state
- [ ] Create Azure Function App
- [ ] Create Application Insights
- [ ] Create Log Analytics Workspace
- [ ] Create Key Vault
- [ ] Create managed identity
- [ ] Add environment-specific variables
- [ ] Add Terraform validate/plan workflow

## 9.2 Application infrastructure modules later

- [ ] Static Web App module for React frontend
- [ ] App Service module for C# API
- [ ] Azure SQL module
- [ ] PostgreSQL module
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

- [ ] Implement auto-merge eligibility service
- [ ] Implement production deployment eligibility service
- [ ] Read GitHub check results
- [ ] Read risk assessment result
- [ ] Read human review state
- [ ] Block auto-deploy on missing/unknown signals
- [ ] Add tests

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

Create a reusable starter template for future React + C# application repositories.

## Template structure

```text
/
├── .github/
│   ├── workflows/
│   ├── ISSUE_TEMPLATE/
│   ├── pull_request_template.md
│   └── CODEOWNERS
├── .ai-sdlc.yml
├── docs/
├── infra/terraform/
├── src/
│   ├── frontend/
│   └── api/
├── tests/
└── README.md
```

## Tasks

- [ ] Create `ai-sdlc-react-dotnet-template` repo
- [ ] Add React frontend using Vite + React Router
- [ ] Add ASP.NET Core Web API
- [ ] Add test structure
- [ ] Add Terraform structure
- [ ] Add GitHub Actions workflows
- [ ] Add `.ai-sdlc.yml`
- [ ] Add mandatory docs
- [ ] Add issue template
- [ ] Add PR template
- [ ] Add CODEOWNERS example
- [ ] Add local dev guide
- [ ] Add build/test verification

---

# 17. Example app repo: `launchcart`

## Goal

Create the first example app that demonstrates the AI SDLC end to end.

## Target stack

- [ ] React frontend
- [ ] C# / ASP.NET Core Web API
- [ ] PostgreSQL initially
- [ ] Terraform
- [ ] GitHub Actions
- [ ] Azure Static Web Apps
- [ ] Azure App Service

## App features

- [ ] Home page
- [ ] Product catalogue
- [ ] Product detail page
- [ ] Purchase enquiry form
- [ ] Admin products page
- [ ] Admin enquiries page
- [ ] Products API
- [ ] Enquiries API
- [ ] PostgreSQL schema
- [ ] Seed data
- [ ] Frontend tests
- [ ] API tests
- [ ] E2E tests
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

- [ ] Architecture overview
- [ ] Local development guide
- [ ] Deployment guide
- [ ] GitHub integration guide
- [ ] Agent/persona guide
- [ ] Risk model guide
- [ ] Audit/logging guide
- [ ] Security/privacy guide
- [ ] Troubleshooting guide
- [ ] Operator runbook

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
