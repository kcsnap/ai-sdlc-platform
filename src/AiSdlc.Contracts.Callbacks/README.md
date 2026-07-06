# AiSdlc.Contracts.Callbacks

Platform-owned contract (ADR-XR-0001 / A11) for the build callbacks the AI SDLC platform POSTs to
yorrixx-app's admin API (`{callbackBaseUrl}/apps/{appId}/{kind}`, header `X-Yorrixx-Admin-Key`):

| kind | payload | notes |
|---|---|---|
| `status` | `StatusCallback` | `status` is a `CanonicalBuildStatus` value (9-value vocabulary) |
| `runtime` | `RuntimeCallback` | `repoUrl` + `hostedUrl`; sent before any `live` status |
| `verification` | `VerificationCallback` | outcome + check table |
| `cost` | `BuildCostCallback` | per-LLM-call raw Anthropic usage |

Wire shape: **camelCase**, nulls omitted, property order = declaration order. Golden-pinned in
`AiSdlc.Orchestrator.Tests/CallbackWireShapeTests` — byte-identical to what the platform sent before the
records were named. Published from `kcsnap/ai-sdlc-platform` (tag `callbacks-v*`).
