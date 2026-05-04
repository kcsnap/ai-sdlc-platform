# AI SDLC Platform TODO

This file is the working checklist for continuing the `ai-sdlc-platform` implementation using Codex CLI.

## Current branch

```text
ai/001-shared-domain-models
```

## Current status

- [x] Repository created: `kcsnap/ai-sdlc-platform`
- [x] Initial scaffold pushed to `main`
- [x] React/C# confirmed as the v1 application target stack
- [x] PR #1 opened for shared domain models
- [x] Duplicate shared model definitions removed from PR branch
- [x] Invalid placeholder test namespaces fixed
- [x] Missing `using Xunit;` imports added to test files
- [ ] Confirm `dotnet build` passes locally
- [ ] Confirm `dotnet test` passes locally
- [ ] Merge PR #1 once build/test passes

---

## Immediate next steps

Run locally from the repository root:

```powershell
cd C:\SnapDev\ai-sdlc-platform
git checkout ai/001-shared-domain-models
git pull
dotnet clean
dotnet restore
dotnet build
dotnet test
```

If the build or tests fail, paste the error into Codex CLI or ChatGPT and fix before merging.

---

## Codex CLI prompt: fix current PR branch

Use this prompt if the current branch still fails to build:

```text
You are working in the ai-sdlc-platform repository on branch ai/001-shared-domain-models.

Goal:
Make the current branch build and test cleanly.

Context:
This is the reusable AI SDLC orchestration platform, not an application repo.
The target application stack for onboarded apps is React frontend and C# / ASP.NET Core backend. Do not introduce Next.js.

Current task:
1. Run dotnet restore.
2. Run dotnet build.
3. Run dotnet test.
4. Fix any compile/test errors.
5. Keep changes minimal and scoped to the current PR.
6. Do not implement real GitHub, Azure, OpenAI, Azure OpenAI, or AI Foundry calls yet.
7. Do not rename projects unless required to make the solution build.
8. Preserve the existing project layout.
9. Commit the fixes with a clear message.

Expected outcome:
- dotnet build passes
- dotnet test passes
```

---

## PR #1: shared domain models

### Purpose

Create the shared vocabulary used by the orchestrator, agents, GitHub integration, audit logging, risk engine and future workflows.

### Models/enums expected in or around `AiSdlc.Shared`

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

### Validation

- [ ] `dotnet build` passes
- [ ] `dotnet test` passes
- [ ] PR reviewed
- [ ] PR merged to `main`

---

## After PR #1 merges

Create a new branch:

```powershell
git checkout main
git pull
git checkout -b ai/002-risk-rules-engine
```

---

# Phase 2: Risk rules engine foundation

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

## Suggested behaviour

- [ ] Accept changed file paths
- [ ] Accept affected areas
- [ ] Accept quality gate results
- [ ] Accept whether Terraform changed
- [ ] Accept whether database migrations changed
- [ ] Accept whether auth/payment/security/privacy areas changed
- [ ] Return `RiskLevel`
- [ ] Return `RiskDecision`
- [ ] Return rationale
- [ ] Return triggered rules/signals

## Initial deterministic rules

- [ ] Docs-only changes default to low risk
- [ ] Tests-only changes default to low risk
- [ ] Simple frontend/content changes default to low risk
- [ ] API changes default to medium risk
- [ ] Database migration changes default to medium risk
- [ ] Terraform changes default to medium risk
- [ ] GitHub Actions workflow changes default to medium risk
- [ ] Authentication/authorisation changes default to high risk
- [ ] Payment/checkout changes default to high risk
- [ ] Personal data handling changes default to high risk
- [ ] Secrets/Key Vault changes default to high risk
- [ ] Failed mandatory quality gates prevent autonomous continuation
- [ ] Unknown/ambiguous signal prevents autonomous continuation

## Codex CLI prompt: risk rules engine

```text
You are working in the ai-sdlc-platform repository.

Create the first deterministic risk rules engine in AiSdlc.Risk.

Requirements:
1. Do not use AI/model calls yet.
2. Use simple C# classes/records/enums.
3. Depend on AiSdlc.Shared where appropriate.
4. Add an IRiskRulesEngine interface.
5. Add a RiskRulesEngine implementation.
6. Add request/result models.
7. Include triggered risk signals in the result.
8. Add xUnit tests covering low, medium and high risk scenarios.
9. Ensure dotnet build and dotnet test pass.
10. Keep implementation simple and extensible.
```

---

# Phase 3: Audit service foundation

## Goal

Create the audit service abstraction and an in-memory implementation for early testing.

## Suggested files

```text
src/AiSdlc.Audit/
  IAuditService.cs
  InMemoryAuditService.cs
  AuditWriteResult.cs

tests/AiSdlc.Audit.Tests/
  InMemoryAuditServiceTests.cs
```

## Tasks

- [ ] Confirm `IAuditService` exists and compiles
- [ ] Create `InMemoryAuditService`
- [ ] Add ability to write an `AuditEvent`
- [ ] Add ability to retrieve events by `RunId`
- [ ] Add tests
- [ ] Defer Azure Storage/Cosmos/Blob implementations

## Codex CLI prompt: audit service

```text
You are working in the ai-sdlc-platform repository.

Create the first audit service implementation.

Requirements:
1. Keep it local/in-memory only for now.
2. Do not add Azure Storage/Cosmos/Blob yet.
3. Use the existing AuditEvent model from AiSdlc.Shared.
4. Add or update IAuditService if needed.
5. Add InMemoryAuditService.
6. Add tests for writing and retrieving events by RunId.
7. Ensure dotnet build and dotnet test pass.
```

---

# Phase 4: GitHub integration contracts

## Goal

Create interfaces and request/response models for future GitHub operations without calling the real GitHub API yet.

## Suggested files

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

## Tasks

- [ ] Define `IGitHubService`
- [ ] Add methods for reading issues/comments
- [ ] Add methods for writing issue/PR comments
- [ ] Add methods for labels
- [ ] Add methods for PR creation
- [ ] Add methods for changed files/check results
- [ ] Add simple tests or compile checks
- [ ] Do not implement live GitHub API calls yet

## Codex CLI prompt: GitHub service contracts

```text
You are working in the ai-sdlc-platform repository.

Create GitHub integration contracts only. Do not call the live GitHub API yet.

Requirements:
1. Add IGitHubService.
2. Add simple request/response models for issues, comments, pull requests, changed files and check results.
3. Use async method signatures and CancellationToken.
4. Keep everything serialisable and testable.
5. Add tests where useful.
6. Ensure dotnet build and dotnet test pass.
```

---

# Phase 5: Agent runtime foundation

## Goal

Create a simple agent execution abstraction that can run persona classes later.

## Suggested files

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

## Tasks

- [ ] Define `IAgent`
- [ ] Define `IAgentRunner`
- [ ] Add `AgentRunner`
- [ ] Add stub persona classes
- [ ] Each stub should return an `AgentResult`
- [ ] No real model calls yet
- [ ] Add unit tests

## Codex CLI prompt: agent runtime

```text
You are working in the ai-sdlc-platform repository.

Create the first agent runtime foundation.

Requirements:
1. Add IAgent and IAgentRunner abstractions.
2. Add an AgentRunner that can execute an agent by name from a registered collection.
3. Add stub agents for ProductStrategistAgent, ProductOwnerAgent and BusinessAnalystAgent.
4. Stubs should return deterministic AgentResult objects.
5. Do not call real AI/model providers yet.
6. Add unit tests for successful agent execution and unknown agent handling.
7. Ensure dotnet build and dotnet test pass.
```

---

# Phase 6: Model provider contracts

## Goal

Create provider interfaces for future Azure OpenAI, AI Foundry and GitHub Copilot integrations.

## Suggested files

```text
src/AiSdlc.ModelProviders/
  IModelProvider.cs
  ModelRequest.cs
  ModelResponse.cs
  ModelProviderOptions.cs
```

## Tasks

- [ ] Add provider abstraction
- [ ] Add request/response models
- [ ] Add stub provider for tests
- [ ] No live provider calls yet

## Codex CLI prompt: model provider contracts

```text
You are working in the ai-sdlc-platform repository.

Create model provider contracts only.

Requirements:
1. Add IModelProvider.
2. Add ModelRequest and ModelResponse models.
3. Add a simple fake/stub provider for tests.
4. Do not call Azure OpenAI, OpenAI, AI Foundry or GitHub Copilot yet.
5. Add unit tests.
6. Ensure dotnet build and dotnet test pass.
```

---

# Phase 7: Durable Functions orchestrator skeleton

## Goal

Create the first Durable Functions orchestration skeleton.

## Suggested files

```text
src/AiSdlc.Orchestrator/
  Functions/
    AiSdlcWorkflowOrchestrator.cs
    GitHubIssueWebhookFunction.cs
    AgentActivityFunctions.cs
```

## Tasks

- [ ] Add orchestrator function skeleton
- [ ] Add HTTP-triggered webhook placeholder
- [ ] Add activity function placeholders
- [ ] No real GitHub webhook validation yet
- [ ] No real agent execution yet unless already available through interfaces
- [ ] Build/test passes

## Codex CLI prompt: orchestrator skeleton

```text
You are working in the ai-sdlc-platform repository.

Create the first Azure Durable Functions orchestrator skeleton.

Requirements:
1. Add an AiSdlcWorkflowOrchestrator function.
2. Add a simple HTTP-triggered GitHubIssueWebhookFunction placeholder.
3. Add activity function placeholders for the first few agents.
4. Use Microsoft.Azure.Functions.Worker isolated worker style.
5. Do not implement real GitHub webhook security yet.
6. Do not implement real model calls yet.
7. Ensure dotnet build and dotnet test pass.
```

---

# Phase 8: GitHub Actions CI

## Goal

Add CI so build/test runs automatically on PRs.

## Suggested file

```text
.github/workflows/ci.yml
```

## Tasks

- [ ] Trigger on pull requests to `main`
- [ ] Trigger on pushes to `main`
- [ ] Setup .NET 8 SDK
- [ ] Run `dotnet restore`
- [ ] Run `dotnet build --no-restore`
- [ ] Run `dotnet test --no-build`

## Codex CLI prompt: CI workflow

```text
You are working in the ai-sdlc-platform repository.

Add a GitHub Actions CI workflow for .NET.

Requirements:
1. Workflow file: .github/workflows/ci.yml
2. Trigger on PRs to main and pushes to main.
3. Use .NET 8 SDK.
4. Run dotnet restore.
5. Run dotnet build --configuration Release --no-restore.
6. Run dotnet test --configuration Release --no-build.
7. Keep the workflow simple.
```

---

# Working rules for Codex

- [ ] Keep each slice small.
- [ ] Create one branch per slice.
- [ ] Make sure `dotnet build` passes before moving on.
- [ ] Make sure `dotnet test` passes before moving on.
- [ ] Do not implement live external integrations until interfaces and tests are stable.
- [ ] Prefer deterministic code before AI/model behaviour.
- [ ] Avoid introducing Next.js; v1 application target stack is React + C#.
- [ ] Keep the platform repo separate from application repos.
- [ ] Use PRs for every meaningful change.

---

# Planned repository model

```text
ai-sdlc-platform
  Reusable AI SDLC orchestration platform.

ai-sdlc-react-dotnet-template
  Future template repo for React + C# application projects.

launchcart
  Future example application repo created from the template.
```
