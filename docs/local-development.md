# Local Development Guide

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 8.0+ | `dotnet --version` |
| Azure Functions Core Tools | v4 | `func --version` |
| Git | Any recent | — |

## Clone and build

```bash
git clone https://github.com/kcsnap/ai-sdlc-platform.git
cd ai-sdlc-platform
dotnet build
```

## Run tests

```bash
dotnet test
```

## Run the Functions host locally

```bash
cd src/AiSdlc.Orchestrator
func start
```

The host listens on `http://localhost:7071` by default.

## Test the Business Analyst endpoint

```bash
curl -X POST http://localhost:7071/api/agents/business-analyst/review \
  -H "Content-Type: application/json" \
  -d '{
    "specTitle": "Add delivery information section",
    "specMarkdown": "## What do you want to create or change?\n\nAdd a delivery info section.\n\n## Why is this needed?\n\nCustomers need delivery expectations.\n\n## Who is the user or customer?\n\nProspective buyers."
  }'
```

## Environment variables

No secrets are required to run the platform locally in its current state. Live external integrations (Azure OpenAI, GitHub API) are not yet wired up.

When they are, secrets will be provided via user secrets or a local `local.settings.json` (which is git-ignored).

## Solution structure

```
src/
  AiSdlc.Shared/          Shared domain models
  AiSdlc.Agents/          Agent runtime, personas, and prompt rendering
  AiSdlc.Orchestrator/    Azure Durable Functions host
  AiSdlc.Risk/            Deterministic risk rules engine
  AiSdlc.Audit/           Audit service abstraction
  AiSdlc.GitHub/          GitHub integration contracts
  AiSdlc.ModelProviders/  AI model provider abstraction

tests/
  AiSdlc.*.Tests/         xUnit test projects mirroring src structure
```
