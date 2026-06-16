# Scaffold-first (Workstream 2) — platform-side plan (locked contract)

> **Audience:** the `ai-sdlc-platform` session (this repo) and, for coordination, the Yorrixx session.
> **Status:** **contract locked 2026-06-16** by platform ↔ Yorrixx agreement. Companion to
> `scaffold-first-and-sweep-gap.md` (Workstream 2).
> **Scope:** platform-owned changes only. Template content (the actual shell components), `auth.spec.ts`,
> drift-restore, provisioning, and the seeding switch are **Yorrixx-owned** — coordinate, don't edit.
> Workstream 1 (the reconciliation-sweep stuck-`Running` rescue) shipped separately as #129/#130.

---

## 0. Source of truth — template repo, seeded by create-from-template

The immutable shell lives in **one place: the template repo `kcsnap/ai-sdlc-react-dotnet-template`.**
A real, tested, buildable shell (with its own green CI) is the whole point of scaffold-first — C#
string constants in Yorrixx's `UserAppScaffold.cs` are exactly the un-compilable, un-testable
fragility this replaces.

This requires a **Yorrixx seeding switch**: `EnsureUserAppRepoAsync` moves from code-gen to
**create-from-template** (GitHub create-from-template / clone), with `UserAppScaffold.cs` reduced to
**per-app overlays** — charter files, `deploy.yml` values, and the charter-derived
`acceptance.spec.ts` stub. Create-from-template **preserves the template's fixed root namespace** (no
per-app rename), which is what makes the immutable `Program.cs` → AI-owned `FeatureRegistration` call
resolve consistently (see §2).

The existing template does **not** match this contract (it ships `ClerkJwtMiddleware`/
`ClerkTokenValidator`, `CosmosClientFactory`/`CosmosItemStore`, `App.tsx`, `lib/api.ts`, an Aspire
AppHost, only `smoke.spec.ts`). So this is a **re-author of the template to the new contract**, not an
additive change, and `auth.spec`/`acceptance.spec` (currently Yorrixx code-gen) move into the template.
Both sessions code against the final tree below.

## 1. The insight that drives sequencing

The three platform changes — protect the shell paths, reframe the Code Implementer, exclude infra
from the manifest — are **not independently shippable**. Protecting `app/AppShell.tsx` *before* the
template seeds it makes the protected-path filter (`AgentActivityFunctions.IsProtectedPath`, applied
at `AiSdlcWorkflowOrchestrator.cs:388` first-build and `:548` PO-fix, and in `FilterRepairChanges`)
silently **drop** the AI's shell file → the app has no entrypoint → every build breaks. Likewise,
reframing the prompt to "don't author auth" before the shell exists ships apps with no auth.

**The enlarged lockstep gate.** Platform protect+reframe must **not merge** until all of:
1. Yorrixx `EnsureUserAppRepoAsync` creates from-template (not code-gen); `UserAppScaffold.cs`
   reduced to per-app overlays; **and**
2. the re-authored template's shell builds **green in template CI**; **and**
3. `auth.spec` / `acceptance.spec` have moved into the template.

The platform changes themselves stay small; the gate is the discipline.

## 2. The seam — locked path contract

| Path | Owner | In manifest? | Protected? |
|---|---|---|---|
| `src/frontend/src/main.tsx` (ClerkProvider wrapping `<AppShell/>`) | Shell | no | **yes** |
| `src/frontend/src/app/AppShell.tsx` (AuthGate: SignedOut→`Sign up`/`Sign in` modal buttons, SignedIn→`data-testid="signed-in"` layout + nav + `<AppRoutes/>` + footer/legal links + error boundary + loading; replaces the now-deleted `App.tsx`) | Shell | no | **yes** |
| `src/frontend/src/lib/api.ts` (`apiUrl` client; reads `VITE_API_BASE_URL`) | Shell | no | **yes** |
| `src/frontend/src/vite-env.d.ts` (`VITE_CLERK_PUBLISHABLE_KEY: string` + `VITE_API_BASE_URL?: string`) | Shell | no | **yes** |
| `src/frontend/src/app/routes.tsx` (ships placeholder `<AppRoutes/>`) | **AI** | yes | no |
| `src/frontend/src/app/nav.ts` (`navItems` the shell renders; empty default) | **AI** | yes | no |
| `src/frontend/src/theme.ts` / Clerk `appearance` (AI-created) | **AI** | yes | no |
| `src/frontend/src/features/**`, `components/ui/**`, `lib/utils.ts` | **AI** | yes | no |
| `src/api/Program.cs` (immutable; calls `FeatureRegistration.AddFeatures(builder.Services)`) | Shell | no | **yes** |
| `src/api/Auth/ClerkJwtMiddleware.cs`, `src/api/Auth/ClerkTokenValidator.cs` (`Api.Auth`) | Shell | no | **yes** |
| `src/api/Data/CosmosClientFactory.cs` (Cosmos client infra, `Api.Data`) | Shell | no | **yes** |
| `src/api/Functions/HealthFunction.cs`, `src/api/host.json`, `src/api/Api.csproj` | Shell | no | **yes** |
| `src/api/Features/FeatureRegistration.cs` (`Api.Features`; static `void AddFeatures(IServiceCollection)`; ships sample `items` registered) | **AI** | yes | no |
| `src/api/Features/**` (new feature services) | **AI** | yes | no |
| `src/api/Data/CosmosItemStore.cs`, `src/api/Functions/ItemsFunction.cs` (sample `items` feature — AI replaces) | **AI** | yes | no |
| `tests/e2e/helpers/auth.ts` (shared register/signOut/signIn helper) | Shell | no | **yes** |
| `tests/e2e/specs/auth.spec.ts` (drives the shell by selector) | Shell | no | **yes** |
| `tests/e2e/specs/acceptance.spec.ts` (charter-derived per-app overlay) | **AI** | yes | **yes-prefix, exempted** |

**Acceptance vs auth specs.** `auth.spec.ts` is immutable shell. `acceptance.spec.ts` is the
charter-derived per-app overlay — the **one** file the Code Implementer authors on build #1 (over the
seeded stub) and maintains **non-regressively** on repair (#115/#117). Its exact path
`tests/e2e/specs/acceptance.spec.ts` is hardcoded in `AgentActivityFunctions.IsAcceptanceSpec` /
`IsAcceptanceSpecRegression`, which exempt it from the `tests/e2e/` protected prefix — **do not move
it**, or platform must change code. Division of labour is unchanged from today.

**Two seams need care, not just a freeze:**

1. **Frontend** — `App.tsx` splits into an immutable `AppShell` + an AI-owned `routes.tsx`/`nav.ts`.
   Styling stays data-driven (`theme.ts` / Clerk `appearance`) so the AI never edits the shell
   components — that is what protects the immutable `auth.spec.ts` selectors.
2. **Backend DI** — `Program.cs` is the backend's `App.tsx`: it wires feature DI, so it can't be
   frozen outright. The seam is a **plain AI-owned static** `FeatureRegistration.AddFeatures` (not a
   C# `partial` method — `void`/visibility constraints make those fragile). The template seeds the
   stub at `src/api/Features/FeatureRegistration.cs` (`namespace Api.Features`,
   `public static void AddFeatures(IServiceCollection services)`, shipping the sample `items` feature
   registered so the template builds green); the immutable `Program.cs` calls
   `FeatureRegistration.AddFeatures(builder.Services)`. Because create-from-template preserves the
   template's root namespace (`Api`), the AI's filled-in version resolves against `Program.cs`'s `using`
   with no rename.

## 3. Platform code changes (this repo, one PR, behind the lockstep gate)

1. **`AgentActivityFunctions.IsProtectedPath` — protect the shell.** Add the shell paths above to a
   new `ShellScaffoldPaths` set alongside `ProtectedPathPrefixes`, as a single source of truth. The
   existing filters at `:388` / `:548` and `FilterRepairChanges` pick it up for free. `IsAcceptanceSpec`
   already exempts `acceptance.spec.ts`; verify the new shell paths against the final template tree.
   **API-side protection is file-level, not prefix-level:** protect `src/api/Program.cs`,
   `src/api/Auth/` (prefix), `src/api/Data/CosmosClientFactory.cs`, `src/api/Functions/HealthFunction.cs`,
   `src/api/host.json`, `src/api/Api.csproj` — but **not** `src/api/Data/` or `src/api/Functions/`
   wholesale, because the sample `items` feature (`Data/CosmosItemStore.cs`, `Functions/ItemsFunction.cs`)
   co-locates with infra and must stay AI-replaceable.

2. **`CodeImplementerAgent` — reframe the contract (the big lever).**
   - Replace `AuthContractDoc` ("here is how to build auth", which still produced `@clerk/react` and
     `RedirectToSignIn` in v004) with a **`ScaffoldContractDoc`** ("DO NOT TOUCH"): the shell already
     exists and is immutable; author only feature pages, domain models, `routes.tsx`, `nav.ts`,
     `theme.ts`, the backend `Features/**`, and `FeatureRegistration.cs`.
   - **Hard line on the backend seam:** quote the template stub's literal root namespace and the
     `static void AddFeatures(IServiceCollection services)` signature, and instruct the AI to preserve
     both and register its feature services there. A missing/renamed `AddFeatures` breaks the immutable
     `Program.cs` build — this is a contract, not advice. (The seeded stub is the canonical source of
     the literal namespace.)
   - Update `ManifestSystemPrompt` to **exclude shell/infra from the plan**. Critical: a manifested
     shell file that the filter then drops would trip the manifest-completeness failure at
     `CodeImplementerAgent.cs:237` ("missing files"). The manifest must never list a protected path.
   - Collapse `LegalLinksDoc` into the shell (the footer now lives in `AppShell`), so the AI stops
     authoring legal links entirely.
   - Mirror the reframe into `BatchSystemPrompt`, `SingleShotSystemPrompt`, `RepairSystemPrompt`.

3. **Tests.** `PersonaAgentTests` smoke; a manifest test asserting no protected path is ever
   manifested; `IsProtectedPath` unit tests for the new shell paths; confirm `FilterRepairChanges`
   drops shell edits while still allowing `FeatureRegistration.cs` and `acceptance.spec.ts`.

## 4. Helper-level contract — per-app Clerk test-user cleanup (id8)

The shared `tests/e2e/helpers/auth.ts` cleans up per-app Clerk test users keyed off the
`e2e-{id8}-` email prefix. The template has no app identity, so the helper derives `id8` from
`APP_ID8` (injected by the verify workflow) → else parses it from the deployed URL
(`app-<slug>-<id8>-frontend`) → else falls back to `tpl`. During the seeding switch, Yorrixx's verify
workflow will **inject `APP_ID8`** for a clean contract; the URL-parse keeps it working until then.
This is template/Yorrixx-owned, recorded here for shared context (no platform code).

## 5. Sequencing

1. **Source-of-truth + path contract** — **DONE** (this document; agreed 2026-06-16).
2. **Step #1 (proof, template-internal)** — **DONE.** Template PR `ai-sdlc-react-dotnet-template#1`
   merged: `tests/e2e/helpers/auth.ts` + immutable `auth.spec.ts` importing it. Killed the flaky
   homegrown `registerUser`. Proves the refactor, not generation.
3. **Step #2 (frontend shell)** — **DONE.** Template PR `#2` merged: `AppShell` + `routes.tsx`/`nav.ts`
   split, `vite-env.d.ts`, legal pages; `App.tsx` deleted, `main.tsx` mounts `AppShell`.
4. **Step #3 (backend seam)** — **DONE.** Template PR `#3` merged: `Api.Features.FeatureRegistration`
   static seam + immutable `Program.cs`. Template `main` now carries the full shell.
5. **Step #4 (seeding switch)** — **NEXT, Yorrixx:** `EnsureUserAppRepoAsync` → generate-from-template;
   `UserAppScaffold.cs` reduced to overlays; drift-restore expanded to the shell paths;
   `DeployWorkflowTemplate` flipped to `VITE_API_BASE_URL` (see §8). Landing green is the platform trigger.
6. **Platform protect+reframe** — ships in the **same release** as the seeding switch landing green (the
   §1 lockstep gate). The tree is now locked, so platform code can be pre-staged (draft) and held until
   the trigger.
7. **Graduate to `@yorrixx/app-kit`** (npm on GitHub Packages) later, once the shell set stabilises.

## 6. Risks on the record

- **Manifest-completeness trap** (change #2) — highest-risk interaction; the manifest prompt and the
  protected-path list must agree exactly, or builds fail on "missing files."
- **Lockstep release** — protect+reframe cannot precede template seeding *and* the Yorrixx seeding
  switch. Do not merge the platform PR until the matching template + Yorrixx PRs are ready.
- **Backend seam namespace** — the reframe must quote the template stub's literal namespace; if the
  template namespace ever changes, the `ScaffoldContractDoc` must be updated in lockstep.
- Unrelated but open: the `AwaitingStageRetry` no-label residual noted in #129/#130.

## 7. Joint ownership

- **Yorrixx:** template content (shell components, `auth.spec`, `FeatureRegistration` stub),
  `acceptance.spec` charter-derived overlay, the `EnsureUserAppRepoAsync` create-from-template switch,
  `UserAppScaffold.cs` per-app overlays, `APP_ID8` injection, provisioning, drift-restore.
- **Platform (this repo):** protected-paths for the shell (`ShellScaffoldPaths`), the Code Implementer
  reframe (`ScaffoldContractDoc` + the backend-seam hard line), the manifest exclusion, the seam contract.

## 8. Seeding switch — create-from-template (Yorrixx spec, 2026-06-16)

**Target.** `EnsureUserAppRepoAsync` moves from `client.Repository.Create` + `UpsertScaffoldFilesAsync`
(code-gen every file) to **generate-from-template** (`POST /repos/{owner}/{repo}/generate`, Octokit
`Repository.Generate`, source = `kcsnap/ai-sdlc-react-dotnet-template`). Every user-app starts as a
full copy of the tested shell. `UserAppScaffold.cs` shrinks to **per-app overlays applied after
generate**.

| Comes from the template (Yorrixx stops emitting) | Per-app overlay (Yorrixx generates, applied post-generate) |
|---|---|
| shell source, `auth.spec.ts`, `tests/e2e/helpers/**`, `playwright.config`, `ci.yml`, `verify.yml`, `Program.cs`, `RepositoryBase`, `FeatureRegistration` stub | `.yorrixx/charter.json` + `yorrixx.spec.md`, `.github/workflows/deploy.yml`, `.yorrixx/platform-contract.md`, `tests/e2e/specs/acceptance.spec.ts` (charter-derived throwing stubs), any per-app Clerk/legal injectables |

**Drift-restore (Yorrixx `EnsureSeededWorkflowsAsync`)** expands to the shell paths — re-fetch the
immutable shell files from the template at verification start so a build can't quietly mutate them.
**Hard constraint:** the restore set is the immutable shell files **only** and **excludes every
per-app overlay** (`deploy.yml`, `acceptance.spec.ts`, `.yorrixx/charter.json`, `platform-contract.md`)
— restoring those would clobber per-app OIDC/charter values. (Independently, the platform's
`IsProtectedPath` already blocks the Code Implementer from touching `.github/` and the shell, so
drift-restore is the backstop against *non-AI* mutation, a separate mechanism.)

**Coordination decisions:**
- **(a) Frontend API env var → `VITE_API_BASE_URL`** (template's name wins over Yorrixx's current
  `VITE_API_URL`). No platform code references either name (grep-confirmed), so this is
  template/Yorrixx-only: Yorrixx bakes `VITE_API_BASE_URL` into `DeployWorkflowTemplate`; the shell's
  `lib/api.ts` + `vite-env.d.ts` declare it; the platform `ScaffoldContractDoc` references it and
  directs the AI to use the `lib/api.ts` client, never read the env var directly.
- **(b) `deploy.yml` stays Yorrixx-generated per-app** — it needs per-app OIDC/resource values and is
  under the always-immutable `.github/`. Must be **excluded from the template-drift-restore set** (see
  above).
- **(c) `.ai-sdlc.yml` provenance — RESOLVED: template-static.** Yorrixx confirmed 2026-06-16. It ships
  from the template as part of the immutable shell (drift-restored), since the config is uniform across
  yorrixx-apps. The platform reads it at RepoIndex; it drives `allow_low_risk_auto_merge` → the
  orchestrator's `allowAutoMerge` (brief auto-approve + bootstrap risk path).

**Platform dependency:** `.yorrixx/charter.json` must exist at run start — applying overlays at
create-time satisfies this. Charter-derived throwing `acceptance.spec.ts` stubs are compatible with the
platform: the implementer authors over them on build #1 (un-gated), and `IsAcceptanceSpecRegression`
runs only on repair against the build-#1 version, not the stub.
