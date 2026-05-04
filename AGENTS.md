# Repository Guidelines

## Project Structure & Module Organization
`AiSdlc.sln` is the entry point for the whole .NET 8 solution. Production code lives under `src/` and is split by responsibility: `AiSdlc.Orchestrator` for the Azure Functions workflow host, `AiSdlc.Agents` for agent definitions, `AiSdlc.GitHub` for GitHub integration, `AiSdlc.Risk` for decision logic, `AiSdlc.Audit` for audit contracts, `AiSdlc.ModelProviders` for model abstractions, and `AiSdlc.Shared` for domain types shared across modules. Tests mirror that structure under `tests/`. Operational and design material belongs in `docs/`, and Azure infrastructure code is under `infra/terraform/`.

## Build, Test, and Development Commands
Use the solution file from the repository root:

- `dotnet restore AiSdlc.sln` restores all projects.
- `dotnet build AiSdlc.sln` compiles every library and test project.
- `dotnet test AiSdlc.sln` runs the xUnit suite across `tests/`.
- `dotnet run --project src/AiSdlc.Orchestrator` starts the orchestrator host locally.
- `terraform fmt infra/terraform` formats Terraform files before review.

## Coding Style & Naming Conventions
Follow `.editorconfig`: 4 spaces for C# and 2 spaces for YAML, JSON, and Markdown; UTF-8; LF endings; final newline required. Nullable reference types and implicit usings are enabled, so keep null handling explicit and avoid redundant imports. Match existing naming: `PascalCase` for types and public members, `camelCase` for locals and parameters, and test classes named after the unit under test, for example `DomainModelsTests`.

## Testing Guidelines
Tests use xUnit with `Microsoft.NET.Test.Sdk` and `coverlet.collector`. Add new test projects under `tests/` only when they map to a `src/` module, otherwise extend the existing module test project. Name tests with behavior-focused methods such as `WorkflowRun_ShouldDefaultRiskValuesToUnknown`. Prefer meaningful assertions over placeholder tests, and run `dotnet test AiSdlc.sln` before opening a PR.

## Commit & Pull Request Guidelines
Recent commits use short, imperative subjects like `Import xUnit in shared domain model tests` and `Add TODO checklist for Codex CLI continuation`. Keep that style: one sentence, present tense, focused on the change. Pull requests should include a concise description, linked issue or task, test evidence, and screenshots only when UI or documentation rendering changes are relevant.

## Security & Configuration Tips
Do not commit secrets, local settings, or generated credentials. Keep deployment-sensitive changes documented in `docs/` and coordinate infrastructure updates in `infra/terraform` with corresponding application changes.
