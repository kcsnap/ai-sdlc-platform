# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Restore, build, test
dotnet restore AiSdlc.sln
dotnet build AiSdlc.sln
dotnet test AiSdlc.sln

# Single test project
dotnet test tests/AiSdlc.Agents.Tests
dotnet test tests/AiSdlc.Shared.Tests

# Run the Functions host locally (requires local.settings.json)
cd src/AiSdlc.Orchestrator && func start

# Run the live-activity dashboard (Blazor Server, http://localhost:5080)
# Reads from the same audit storage the orchestrator writes to.
# Default config uses Azurite (UseDevelopmentStorage=true) — see appsettings.json.
cd src/AiSdlc.Dashboard && dotnet run
```

CI runs on .NET 8, Ubuntu, Release configuration. `TreatWarningsAsErrors` is enabled globally — the build will fail on any warning.

## Architecture

This is a .NET 8 Azure Durable Functions platform that runs an AI-driven SDLC review pipeline triggered by GitHub webhooks.

**Request flow:**

```
GitHub Issue Opened
  → GitHubWebhookFunction (HMAC-SHA256 validation)
    → AiSdlcWorkflowOrchestrator (Durable orchestrator)
        1. RepoIndex — reads .ai-sdlc.yml from the source repo
        2. ProductStrategist, ProductOwner (waits for human approval), BusinessAnalyst
        3. Architect
        4. Parallel fan-out: Security, UX, DevOps, Content, Compliance, Analytics, QA, SeniorCoder
        5. RiskAssessor — deterministic rules engine (AiSdlc.Risk), no AI
        6. ReleaseManager
    All agent outputs are posted as GitHub comments and logged via IAuditService.
```

**Projects and responsibilities:**

| Project | Role |
|---|---|
| `AiSdlc.Orchestrator` | Azure Functions host, DI wiring, webhook entry point |
| `AiSdlc.Agents` | `IAgent`/`AgentRunner`; all 14 persona agents |
| `AiSdlc.Shared` | Domain models (`AgentContext`, `AgentResult`, `WorkflowRun`), `RegexRedactionService` |
| `AiSdlc.ModelProviders` | `IModelProvider` abstraction; `AnthropicModelProvider` (with prompt redaction), `FakeModelProvider` |
| `AiSdlc.Risk` | Deterministic file-pattern risk rules → `RiskLevel`/`RiskDecision` |
| `AiSdlc.Audit` | `IAuditService` backed by Azure Table Storage + Blob |
| `AiSdlc.GitHub` | `IGitHubService` / `GitHubApiClient` |
| `AiSdlc.RepoIndex` | `IRepoIndexer` / `GitHubRepoIndexer` reads `.ai-sdlc.yml` |
| `AiSdlc.Dashboard` | Blazor Server view-only dashboard; tails `AuditEvents` table and streams to browser via the built-in Blazor circuit |

Dependency flow: Orchestrator → everything. Agents → Shared + ModelProviders. All others → Shared only.

## Key conventions

**Nullable & warnings:** Nullable reference types are enabled everywhere. Eliminate all nullability warnings — the build treats them as errors.

**Dependency injection:** All agents are registered as `IAgent` singletons in `Program.cs`. Add new agents there in alphabetical order alongside the existing 14. `AgentRunner` dispatches by `AgentNames` constant.

**Adding a new persona agent:**
1. Create `src/AiSdlc.Agents/Personas/MyNewAgent.cs` implementing `IAgent`.
2. Add a constant to `AgentNames.cs`.
3. Register `services.AddSingleton<IAgent, MyNewAgent>()` in `Program.cs`.
4. Wire into the orchestrator fan-out in `AiSdlcWorkflowOrchestrator.cs`.
5. Add a smoke test in `tests/AiSdlc.Agents.Tests/PersonaAgentTests.cs` using `FakeModel()`.

**Testing:** xUnit throughout. Tests mirror src layout — one test project per src project. Use `FakeModelProvider` (never live Anthropic calls in tests), `NoOpGitHubService`, and `NoOpRepoIndexer`. Smoke tests assert `Status == "Completed"` and non-empty output. Use `MakeRequest()` factory helpers for test fixtures.

**Prompt redaction:** `AnthropicModelProvider` automatically redacts 15 regex patterns (API keys, tokens, PII) before every API call. No special handling needed at the agent layer.

**Local secrets:** `src/AiSdlc.Orchestrator/local.settings.json` is git-ignored. Required keys: `AnthropicApiKey`, `AnthropicModel`, `GitHubPat`, `GitHubWebhookSecret`, `AuditStorageAccountName`.

**Code style:** 4-space indent (C#), 2-space (YAML/JSON/Markdown), LF line endings, UTF-8, final newline. PascalCase for types and public members, camelCase for locals/parameters.

**Branch naming:** `{ai|feat|fix|docs|chore}/{issue#}-{slug}` (enforced by the `verify-issue-link` workflow). Slug is kebab-case `[a-z0-9][a-z0-9-]*`. Example: `feat/49-bootstrap-terminal-markers`. The issue must exist and be OPEN.

## Infrastructure

Terraform in `infra/terraform/` provisions: Azure Function App, Storage Account, Key Vault, Application Insights, Log Analytics, Managed Identity. Auth uses `DefaultAzureCredential` (managed identity in Azure, interactive/env fallback locally).
