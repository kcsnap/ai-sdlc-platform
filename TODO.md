# AI SDLC Platform TODO

This file is the working checklist for continuing the `ai-sdlc-platform` implementation using Codex CLI.

## Current branch

```text
ai/002-risk-rules-engine
```

## Current status

- [x] Repository created: `kcsnap/ai-sdlc-platform`
- [x] Initial scaffold pushed to `main`
- [x] React/C# confirmed as the v1 application target stack
- [x] PR #1 opened for shared domain models
- [x] Duplicate shared model definitions removed from PR branch
- [x] Invalid placeholder test namespaces fixed
- [x] Missing `using Xunit;` imports added to test files
- [x] Confirm `dotnet build` passes locally
- [x] Confirm `dotnet test` passes locally
- [x] Merge PR #1 once build/test passes

---

## Immediate next steps

Current branch status:

```powershell
cd C:\SnapDev\ai-sdlc-platform
dotnet build
dotnet test
```

Then:

1. Open a PR for `ai/002-risk-rules-engine`.
2. Review the branch as one combined foundation slice covering phases 2-8.
3. Merge once CI passes.

---

## Current session summary

- [x] Phase 2 complete: deterministic risk rules engine
- [x] Phase 3 complete: in-memory audit service
- [x] Phase 4 complete: GitHub service contracts
- [x] Phase 5 complete: agent runtime foundation
- [x] Phase 6 complete: model provider contracts
- [x] Phase 7 complete: Durable Functions orchestrator skeleton
- [x] Phase 8 complete: GitHub Actions CI workflow

Remaining intentional defer items:

- [ ] Do not add Azure Storage/Cosmos/Blob audit implementations yet
- [ ] Do not implement live GitHub API calls yet

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

- [x] `dotnet build` passes
- [x] `dotnet test` passes
- [x] PR reviewed
- [x] PR merged to `main`

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

- [x] Accept changed file paths
- [x] Accept affected areas
- [x] Accept quality gate results
- [x] Accept whether Terraform changed
- [x] Accept whether database migrations changed
- [x] Accept whether auth/payment/security/privacy areas changed
- [x] Return `RiskLevel`
- [x] Return `RiskDecision`
- [x] Return rationale
- [x] Return triggered rules/signals

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

- [x] Confirm `IAuditService` exists and compiles
- [x] Create `InMemoryAuditService`
- [x] Add ability to write an `AuditEvent`
- [x] Add ability to retrieve events by `RunId`
- [x] Add tests
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

- [x] Define `IGitHubService`
- [x] Add methods for reading issues/comments
- [x] Add methods for writing issue/PR comments
- [x] Add methods for labels
- [x] Add methods for PR creation
- [x] Add methods for changed files/check results
- [x] Add simple tests or compile checks
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

- [x] Define `IAgent`
- [x] Define `IAgentRunner`
- [x] Add `AgentRunner`
- [x] Add stub persona classes
- [x] Each stub should return an `AgentResult`
- [x] No real model calls yet
- [x] Add unit tests

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

- [x] Add provider abstraction
- [x] Add request/response models
- [x] Add stub provider for tests
- [x] No live provider calls yet

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

- [x] Add orchestrator function skeleton
- [x] Add HTTP-triggered webhook placeholder
- [x] Add activity function placeholders
- [x] No real GitHub webhook validation yet
- [x] No real agent execution yet unless already available through interfaces
- [x] Build/test passes

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

- [x] Trigger on pull requests to `main`
- [x] Trigger on pushes to `main`
- [x] Setup .NET 8 SDK
- [x] Run `dotnet restore`
- [x] Run `dotnet build --no-restore`
- [x] Run `dotnet test --no-build`

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

- [x] Keep each slice small.
- [x] Create one branch per slice.
- [x] Make sure `dotnet build` passes before moving on.
- [x] Make sure `dotnet test` passes before moving on.
- [ ] Do not implement live external integrations until interfaces and tests are stable.
- [x] Prefer deterministic code before AI/model behaviour.
- [x] Avoid introducing Next.js; v1 application target stack is React + C#.
- [x] Keep the platform repo separate from application repos.
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
