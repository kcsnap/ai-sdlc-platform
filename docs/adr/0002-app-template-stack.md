# ADR 0002: Generated-App Template Stack

## Status

Accepted (2026-06-02)

## Context

The platform generates user-apps. Until now the stack for those apps has been documented inconsistently:

- **TODO.md §16/§17 + README.md** still listed PostgreSQL + Azure App Service for API
- The **yorrixx-app architecture memory** said Cosmos serverless + Azure Container Apps + Resend
- The parallel yorrixx-app session's just-shipped `HostingService` provisioned Container Apps + Cosmos

Three sources, three different stacks. The first user-app build worked but no single document said what the next one should look like, and the existing infra code was about to be extended in the Container Apps direction before this contradiction was caught.

This ADR consolidates the user-app stack with a conservative, cost-minimised choice optimised for "first user-app free, paid plan later". It supersedes all prior decisions on this topic.

## Decision

### Cloud, region, compute, data

| Dimension | Choice |
|---|---|
| Cloud | Azure |
| Region | UK South (single region, v1) |
| Frontend hosting | Azure Web App, **F1 Free tier** — separate Web App from API |
| API hosting | Azure Web App, **F1 Free tier** — separate Web App from frontend |
| App Service Plan | One **F1 Plan per user-app** hosting both Web Apps (F1 cannot share plans) |
| Database engine | Cosmos DB |
| Cosmos account | Single shared serverless account in Yorrixx subscription |
| Cosmos database | `userapps` (single shared) |
| Cosmos container per app | `app-{appNameSlug8}-{appId8}`, partition key `/id` |
| Cosmos RBAC | Per-app Web App MI granted **Cosmos DB Built-in Data Contributor** scoped to its own container, plus account-level `Microsoft.DocumentDB/databaseAccounts/readMetadata` for endpoint discovery |

### Identity, integrations, observability, secrets

| Dimension | Choice |
|---|---|
| End-user auth | **Clerk** (single Yorrixx-owned instance) with **Clerk Organizations** — one Org per user-app, per-app branding, builder = Org admin, cross-app first visit requires explicit "Join this app?" consent |
| Payments | **Stripe — deferred to v2.** Template ships no Stripe wiring |
| Email transport | **SendGrid** |
| Email templates | In user-app code (Razor/Markdown), version-controlled |
| Telemetry | App Insights — one component per user-app |
| Log workspace | Single shared Log Analytics workspace in Yorrixx subscription |
| Secrets store | Single shared Yorrixx Key Vault (`kv-yorrixx-dev`); per-app secrets prefixed `app-{appId8}--{secret}`; per-app MI RBAC-scoped to its own prefix |

### Runtime versions

| | |
|---|---|
| .NET | **9** (STS) |
| API pattern | Minimal APIs |
| Node | 22 LTS |
| React | 19 |

### Per-user-app repository

| Dimension | Choice |
|---|---|
| Repo structure | Mono-repo per user-app — `src/frontend/`, `src/api/`, `src/AppHost/` (dev-only), `tests/`, `.github/workflows/`. **No `infra/`** in user-app repo |
| Frontend libs | React 19 + Vite + TypeScript + TanStack Query + Tailwind + shadcn/ui (matches Yorrixx itself) |
| API project | Vanilla ASP.NET Core minimal API. `Program.cs` reads from `appsettings.json` + env vars. **No Aspire dependencies** in the API project — runs standalone in F1 |
| AppHost project | **Local-dev only**, never deployed. .NET Aspire AppHost orchestrates API + Vite + Cosmos emulator. Excluded from `deploy.yml` |
| Local-dev secrets | `dotnet user-secrets` for API + `.env.local` for Vite |
| Test mandate (v1) | Unit tests required (Vitest + xUnit). Integration + E2E (Playwright + Cosmos emulator) scaffolded but not CI-gated |

### CI/CD

| File | Purpose |
|---|---|
| `.github/workflows/ci.yml` | Build + test on every PR (`dotnet build/test` for API + AppHost, `npm run build/test` for frontend) |
| `.github/workflows/deploy.yml` | Deploy on push to main: build → zip → `azure/webapps-deploy@v3` (run-from-package) for each of frontend + API Web App. AppHost is NOT deployed |
| Auth from CI | OIDC via federated credential — no long-lived secrets in the repo |

### Infrastructure ownership

| | |
|---|---|
| Provisioning | **Centralised** — Yorrixx's Hosting module creates every Azure resource per user-app plus the Clerk Org |
| User-app repo | Code + tests + `.github/workflows/`. No `infra/` directory |
| Provisioning order | 1. Create GitHub repo `yorrixx-apps/user-app-{id}` (so deploy-SP federated credential subject is valid on first push). 2. Create Clerk Org + add builder as admin. 3. Create Azure resources. 4. Create deploy SP + federated credential. 5. Inject Clerk publishable key + App Insights connection string + Cosmos endpoint into the two Web Apps' app settings. 6. Seed initial repo commit (charter + template files + `.github/workflows/`) |
| Per-app deploy SP | One SP `sp-userapp-{appId8}` with one federated credential bound to `repo:yorrixx-apps/user-app-{id}:ref:refs/heads/main`. Both Web App deploys use this SP |

### Azure resource naming convention

Format: `{kind}-{appNameSlug8}-{appId8}[-suffix]` where `appNameSlug8` is the first 8 chars of the `[a-z0-9]` slug of the charter app name (fallback `app`) and `appId8` is the first 8 hex of the GUID-no-hyphens.

| Resource | Example |
|---|---|
| App Service Plan | `plan-sport121-b80683eb` |
| Frontend Web App | `app-sport121-b80683eb-frontend` |
| API Web App | `app-sport121-b80683eb-api` |
| Managed Identity | `id-sport121-b80683eb` |
| Cosmos container | `app-sport121-b80683eb` |
| App Insights | `appi-sport121-b80683eb` |
| KV secret prefix | `app-b80683eb--` (in shared `kv-yorrixx-dev`) |
| Deploy SP | `sp-userapp-b80683eb` |

## Supersedes

| Prior decision | Source | New decision (this ADR) |
|---|---|---|
| Container Apps for user-app API (scale-to-zero) | Architecture memory #10; yorrixx-app `HostingService.cs` | Azure Web App (F1) per-app for API |
| Static Web Apps for user-app frontend | Architecture memory #15; Stage 1 plan | Azure Web App (F1) per-app for frontend |
| Resend for email | Architecture memory #9; OPEN_QUESTIONS Q9 (2026-05-30) | SendGrid |
| Postgres for launchcart | TODO §17 | Cosmos |
| .NET 10 for user-app API | Architecture memory #3 | .NET 9 |

## Consequences

### Known limits (accepted v1 tradeoffs)

- **F1 60-min/day CPU quota per Web App.** Two Web Apps per user-app = 120 min/day combined. A demo with realistic traffic will hit this and return 403 until midnight UTC. This is the earliest trigger for B1 upgrade.
- **F1 5-Web-Apps-per-subscription-per-region cap.** Two Web Apps per user-app = max ~5 user-apps before forced B1 upgrade. Tracked as a v2 trigger.
- **F1 no Always On.** ~5–10 s cold start after 20 min idle. Documented in user-app README + frontend surfaces a "just woke up" loading state.
- **F1 no SSL on custom domains.** User-apps stay on `*.azurewebsites.net` URLs in v1. `*.apps.yorrixx.io` is a v2 / B1 upgrade trigger.
- **F1 1 GB storage cap per app.** Code fits; user-apps must not write files to disk. Logs go to App Insights; uploads to blob or Cosmos only.
- **F1 no scale, no slots, no daily backups, no HTTP/2.**
- **Cosmos serverless container cap ~100 per account.** Hosting service shards to a new serverless account beyond that (post-v1).
- **Cosmos serverless noisy-neighbour.** One app's bursty RU usage can affect overall account latency. Accepted v1 tradeoff inherent to "single shared account".
- **Clerk B2B SaaS plan required** for Organizations (~$25/mo + per-MAU). Free Clerk tier doesn't include Orgs. Confirm pricing before shipping.

### Downstream work

- **Yorrixx-side rebuild required.** The just-shipped `Yorrixx.Modules.Hosting/Internal/HostingService.cs` provisions Container Apps + Cosmos. It must be rewritten per this ADR: F1 Plan + 2 Web Apps + Cosmos container + KV secret RBAC + App Insights + MIs + deploy SP + Clerk Org, in the order specified above. Handoff prompt for the yorrixx-app session covers this.
- **Template repo creation.** When `ai-sdlc-react-dotnet-template` is created (TODO §16), its layout + tooling must match this ADR. Its own `.ai-sdlc.yml` is where the user-app stack values live (the platform's own `.ai-sdlc.yml` correctly describes the platform itself and is intentionally not modified by this ADR).
- **Memory supersede notes** added to `project_yorrixx_app_architecture.md` decisions #3, #5, #9, #10, #15 pointing here.
