# ADR 0003: User-App API Tier on Azure Functions Flex Consumption

## Status

Accepted (2026-06-02)

## Context

[ADR-0002](0002-app-template-stack.md) locked the user-app stack at two F1 Azure Web Apps per user-app (frontend + API on one F1 App Service Plan). It explicitly named the F1 60-min/day CPU quota as the earliest trigger for B1 upgrade, and the 5-F1-Web-Apps-per-subscription-per-region cap as the v2 ceiling.

Both constraints fall disproportionately on the API tier:

- The API does the LLM-fronting and Cosmos R/W. Any real demo traffic burns the 60-min/day quota fast.
- Two Web Apps per user-app halves the 5-app cap to ~2.5 user-apps before forced B1 upgrade.

The frontend, by contrast, serves mostly static compiled assets and sits comfortably inside F1 indefinitely.

Azure Functions Flex Consumption (GA in UK South since 2024) offers a better fit for the API tier specifically:

- 100k executions + GB-s grant per Function App per month (Flex Consumption's free grant is per-app, not per-subscription).
- True scale-to-zero with cold start ~1–2 s on .NET 9 isolated (vs F1's 5–10 s after 20-min idle).
- Optional always-ready instances per app for sub-second cold start when needed.
- VNet integration and predictable pricing model.
- HTTP triggers via ASP.NET Core integration (`ConfigureFunctionsWebApplication()`) keep the minimal-API programming model substantively unchanged from ADR-0002.

This ADR amends ADR-0002 by switching **only** the API tier to Flex Consumption. Frontend, data, identity, telemetry, secrets, runtime versions, CI/CD authentication, infrastructure ownership, and provisioning order all remain as in ADR-0002.

## Decision

### Compute (API tier only)

| Dimension | Choice |
|---|---|
| API hosting | **Azure Functions, Flex Consumption plan** — one Function App per user-app |
| Functions plan | One Flex Consumption plan per user-app (`flex-{appNameSlug8}-{appId8}`) |
| Functions runtime | .NET 9 isolated worker; ASP.NET Core integration via `ConfigureFunctionsWebApplication()` so endpoints look like minimal-API routes |
| Always-ready instances | 0 (v1 default). Bump to 1 on a per-app basis when cold start becomes a complaint |
| Function App storage | Per-app general-purpose v2 storage account `st{appId8}` (lowercase, no hyphens — Azure naming rule), used for `AzureWebJobsStorage` only |

### Frontend tier (unchanged from ADR-0002)

| Dimension | Choice |
|---|---|
| Frontend hosting | Azure Web App, **F1 Free tier** — unchanged |
| App Service Plan | One **F1 Plan per user-app** hosting the single frontend Web App |

### Resource naming (amends ADR-0002)

| Resource | Old (ADR-0002) | New (this ADR) |
|---|---|---|
| API compute | `app-{appNameSlug8}-{appId8}-api` (Web App) | `func-{appNameSlug8}-{appId8}` (Function App) |
| API plan | (shared F1 plan) | `flex-{appNameSlug8}-{appId8}` (Flex Consumption plan) |
| API storage | (none — F1 disk) | `st{appId8}` (general-purpose v2; Functions runtime only) |

Example for `app-sport121-b80683eb`:

| Resource | Name |
|---|---|
| Frontend Web App | `app-sport121-b80683eb-frontend` |
| Frontend F1 plan | `plan-sport121-b80683eb` |
| API Function App | `func-sport121-b80683eb` |
| API Flex plan | `flex-sport121-b80683eb` |
| API storage | `stb80683eb` |

All other resources (Managed Identity, App Insights, Cosmos container, KV secret prefix, deploy SP) unchanged from ADR-0002.

### CI/CD (amends ADR-0002)

`.github/workflows/deploy.yml` deploys both compute targets on push to `main`:

- **Frontend:** `azure/webapps-deploy@v3` (zip + run-from-package) — unchanged.
- **API:** `azure/functions-action@v1` (zip deploy to Function App) — replaces `azure/webapps-deploy@v3` for the API.
- OIDC federated credential subject pattern is identical; the same per-app deploy SP authenticates both deploys.

### Identity, RBAC (unchanged from ADR-0002)

Per-app Managed Identity has:

- **Cosmos DB Built-in Data Contributor** scoped to its own container, plus account-level `readMetadata`.
- **Key Vault Secrets User** scoped to its own `app-{appId8}--` prefix.
- The MI is assigned to the Function App's `identity.userAssignedIdentities` (same as Web App in ADR-0002).

### Provisioning order (amends step 3, 5, 6 of ADR-0002)

1. Create GitHub repo `yorrixx-apps/user-app-{id}`.
2. Create Clerk Org + add builder as admin.
3. Create Azure resources: F1 Plan, frontend Web App, Flex Consumption plan, Function App, Function App storage account, Managed Identity, App Insights component.
4. Create deploy SP + federated credential bound to `repo:yorrixx-apps/user-app-{id}:ref:refs/heads/main`.
5. Inject Clerk publishable key + App Insights connection string + Cosmos endpoint into the **frontend Web App's app settings and the Function App's app settings** (both targets need the same configuration injection).
6. Seed initial repo commit (charter + template files + `.github/workflows/`).

## Supersedes (rows carved out from ADR-0002)

| Prior decision (ADR-0002) | New decision (this ADR) |
|---|---|
| API hosting: Azure Web App, F1 Free tier | Azure Functions, Flex Consumption plan |
| App Service Plan: One F1 Plan per user-app hosting both Web Apps | One F1 Plan per user-app for the frontend only; one Flex Consumption plan per user-app for the API |
| API Web App naming `app-{appNameSlug8}-{appId8}-api` | Function App naming `func-{appNameSlug8}-{appId8}` |
| Deploy action for API: `azure/webapps-deploy@v3` | `azure/functions-action@v1` |
| API project "runs standalone in F1" (vanilla minimal API) | API project runs on Functions isolated worker with `ConfigureFunctionsWebApplication()`; route handlers stay minimal-API-style |

All other ADR-0002 decisions remain authoritative.

## Consequences

### Improvements vs ADR-0002

- **API tier free-grant:** 100k executions + 400k GB-s per Function App per month (per-app, not per-subscription). Substantially extends "first user-app free" duration for API-bound workloads.
- **API cold start:** 1–2 s on Flex Consumption .NET 9 isolated (vs 5–10 s on F1 after 20-min idle).
- **Region cap headroom:** API moves off the 5-F1-Web-Apps-per-subscription-per-region cap. Cap now applies only to frontends — ~5 user-apps free before forced upgrade (up from ~2.5).
- **API CPU quota:** Eliminated. Flex Consumption bills per-instance-second when active; no daily quota.
- **No always-on cost for API.** Flex Consumption is true scale-to-zero with `0` always-ready instances.

### Known limits (accepted v1 tradeoffs)

- **HTTP request gateway timeout: 230 s** (Azure Front Door / ARR enforced) and Function execution max 10 min. LLM responses must stream or chunk; long single-shot generations could clip. Template documents this in the API project README.
- **One extra resource per user-app:** Function App storage account (`st{appId8}`). Trivial cost (<$1/mo at idle) but counts toward subscription storage-account quotas (250/region by default — non-issue at v1 scale).
- **Aspire AppHost local-dev:** Aspire 9 supports Functions; expect slightly more friction than the Web App + Aspire flow. Documented in user-app AppHost README.
- **Flex Consumption regional availability:** UK South confirmed GA. If a future region is added without Flex Consumption GA, fall back to regular Consumption per region.
- **HTTP/2, SSL on custom domains, etc.** still tied to upgrade triggers as in ADR-0002 — but the API side now upgrades independently of the frontend.

### Downstream work

- **Yorrixx-side rebuild scope updated.** The pending `Yorrixx.Modules.Hosting/Internal/HostingService.cs` rewrite now provisions: F1 Plan + 1 Web App (frontend) + 1 Flex Consumption plan + 1 Function App + 1 storage account + KV secret RBAC + App Insights + MI + deploy SP + Clerk Org. The handoff prompt to the yorrixx-app session needs updating before paste (or a follow-up note if already pasted).
- **Template repo `ai-sdlc-react-dotnet-template`.** When created (TODO §16), its API project uses Functions isolated worker + `ConfigureFunctionsWebApplication()`. CI/CD workflow uses `azure/functions-action@v1` for the API.
- **README + TODO + architecture memory updates** land alongside this ADR.
