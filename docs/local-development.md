# Local Development Guide

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 8.0+ | `dotnet --version` |
| Azure Functions Core Tools | v4 | `func --version` |
| Git | Any recent | — |
| ngrok (optional) | Any | For webhook testing |

## Clone and build

```bash
git clone https://github.com/kcsnap/ai-sdlc-platform.git
cd ai-sdlc-platform
dotnet build
dotnet test
```

## Run the Functions host locally

```bash
cd src/AiSdlc.Orchestrator
func start
```

The host listens on `http://localhost:7071` by default.

## Environment variables

Copy `src/AiSdlc.Orchestrator/local.settings.json.example` to `local.settings.json` and fill in your values. This file is git-ignored and must never be committed.

| Variable | Required locally | Notes |
|----------|-----------------|-------|
| `AnthropicApiKey` | Yes | Anthropic API key |
| `AnthropicModel` | No | Defaults to `claude-haiku-4-5-20251001` |
| `GitHubPat` | Yes | PAT with `repo` scope |
| `GitHubWebhookSecret` | No | Leave blank to skip HMAC validation locally |
| `AuditStorageAccountName` | Yes | Azure Storage account (use Azurite locally) |

## Webhook testing with ngrok

1. Start the Functions host: `func start`
2. In a separate terminal: `ngrok http 7071`
3. Copy the ngrok HTTPS URL.
4. Go to your GitHub repo → Settings → Webhooks → Edit.
5. Set the Payload URL to `https://<your-ngrok-id>.ngrok-free.app/api/github/webhook`
6. Set Content type to `application/json`
7. Set Secret to match `GitHubWebhookSecret` in `local.settings.json` (or leave both blank for local testing)
8. Select the `Issues` event.
9. Save the webhook, then open a new issue in the repo to trigger the workflow.

## Solution structure

```
src/
  AiSdlc.Shared/          Shared domain models, redaction, auto-merge eligibility
  AiSdlc.Agents/          Agent runtime, personas, prompt building
  AiSdlc.Orchestrator/    Azure Durable Functions host — orchestration and webhooks
  AiSdlc.Risk/            Deterministic risk rules engine (no AI)
  AiSdlc.Audit/           Audit service abstraction and Azure Table/Blob implementations
  AiSdlc.GitHub/          GitHub API client and service contracts
  AiSdlc.ModelProviders/  AI model provider abstraction and Anthropic implementation
  AiSdlc.RepoIndex/       Repo knowledge indexer — reads .ai-sdlc.yml

tests/
  AiSdlc.*.Tests/         xUnit test projects mirroring src structure
```

## Running specific test projects

```bash
dotnet test tests/AiSdlc.Agents.Tests
dotnet test tests/AiSdlc.Shared.Tests
dotnet test tests/AiSdlc.Orchestrator.Tests
```

The Azure integration tests in `AiSdlc.Audit.Tests` are skipped by default because they require a live Azure Storage account. To run them, set `AuditStorageConnectionString` in the test environment.

## Adding a new persona agent

1. Create `src/AiSdlc.Agents/Personas/MyNewAgent.cs` implementing `IAgent`.
2. Add `AgentNames.MyNewAgent` constant to `AgentNames.cs`.
3. Register in `Program.cs`: `services.AddSingleton<IAgent, MyNewAgent>()`.
4. Add an activity in `AgentActivityFunctions.cs`.
5. Wire the activity into `AiSdlcWorkflowOrchestrator.cs`.
6. Add a test in `PersonaAgentTests.cs`.
7. Add the agent to `BuildActivityFunctions()` in `OrchestratorSkeletonTests.cs`.
