# Scaffold-first (Workstream 2) — platform-side plan

> **Audience:** the `ai-sdlc-platform` session (this repo) and, for coordination, the Yorrixx session.
> **Status:** written 2026-06-16. Companion to `scaffold-first-and-sweep-gap.md` (Workstream 2).
> **Scope:** platform-owned changes only. Template content (the actual shell components),
> `auth.spec.ts`, drift-restore and provisioning are **Yorrixx-owned** — coordinate, don't edit.
> Workstream 1 (the reconciliation-sweep stuck-`Running` rescue) shipped separately as #129/#130.

---

## The insight that drives sequencing

Three platform changes are needed — protect the shell paths, reframe the Code Implementer, and
adjust the manifest — but they are **not independently shippable**. Protecting
`src/frontend/src/App.tsx` *before* the template seeds a shell there makes the protected-path
filter (`AgentActivityFunctions.IsProtectedPath`, applied at `AiSdlcWorkflowOrchestrator.cs:388`
first-build and `:548` PO-fix, and in `FilterRepairChanges`) silently **drop** the AI's `App.tsx`
→ the app has no entrypoint → every build breaks. Likewise, reframing the prompt to "don't author
auth" before the shell exists ships apps with no auth.

**Therefore the platform protect+reframe and the Yorrixx template seeding must go live in one
coordinated release.** That gate shapes the whole plan: agree the path contract first, then ship
each layer in lockstep.

## The seam — path contract (platform ↔ Yorrixx must agree before coding)

| Path | Owner | In manifest? | Protected? |
|---|---|---|---|
| `src/frontend/src/main.tsx` (renders `<AppShell/>`) | Shell | no | **yes** |
| `src/frontend/src/app/AppShell.tsx` (ClerkProvider, AuthGate, layout, footer + legal links, error boundary, loading) | Shell | no | **yes** |
| `src/frontend/src/lib/apiClient.ts` (`apiUrl`) | Shell | no | **yes** |
| `src/frontend/src/vite-env.d.ts` (the `VITE_CLERK_PUBLISHABLE_KEY: string` fix) | Shell | no | **yes** |
| `src/frontend/src/app/routes.tsx` | **AI** | yes | no |
| `src/frontend/src/app/nav.ts` (list the shell consumes) | **AI** | yes | no |
| `src/frontend/src/theme.ts` / Clerk `appearance` | **AI** | yes | no |
| `src/frontend/src/features/**` (pages, components) | **AI** | yes | no |
| `src/api/Auth/ClerkAuthHandler.cs` | Shell | no | **yes** |
| `src/api/Data/RepositoryBase.cs` + repo interfaces | Shell | no | **yes** |
| `src/api/Features/**` (controllers, domain models, typed queries) | **AI** | yes | no |
| `tests/e2e/**` (incl. new `helpers/auth.ts`) | Yorrixx | no | already protected |

**Two seams need care, not just a freeze:**

1. **Frontend** — `App.tsx` splits into an immutable `AppShell` + an AI-owned `routes.tsx`/`nav.ts`
   (the roadmap's explicit seam). Styling stays data-driven (`theme.ts` / Clerk `appearance`) so the
   AI never edits the shell components — that is what protects the immutable `auth.spec.ts` selectors.
2. **Backend DI** — `Program.cs` is the backend's `App.tsx`: it wires DI for feature controllers and
   repositories, so it cannot be fully frozen. It needs the same split — an **immutable `Program.cs`**
   that calls a generated `FeatureRegistration.AddFeatures(services)` partial the AI owns. This is
   **not in the roadmap doc**; it is the backend analog being proposed here. Without it, either
   `Program.cs` is AI-owned (and infra regeneration returns — the v004 disease) or it is frozen (and
   features cannot register). Recommended: the partial-method registration seam. **Open design
   question pending Yorrixx buy-in.**

## Platform code changes (this repo, one PR, behind the coordination gate)

1. **`AgentActivityFunctions.IsProtectedPath` — protect the shell.** Add the shell paths above to a
   new `ShellScaffoldPaths` set alongside `ProtectedPathPrefixes`, as a single source of truth. The
   existing filters at `:388` / `:548` and `FilterRepairChanges` pick it up for free. Verify exact
   paths/casing against the real template before coding.

2. **`CodeImplementerAgent` — reframe the contract (the big lever).**
   - Replace `AuthContractDoc` ("here is how to build auth", which still produced `@clerk/react` and
     `RedirectToSignIn` in v004) with a **`ScaffoldContractDoc`** ("DO NOT TOUCH"): the shell already
     exists and is immutable; author only feature pages, domain models, `routes.tsx`, `nav.ts`,
     `theme.ts`, and the backend `FeatureRegistration` partial.
   - Update `ManifestSystemPrompt` to **exclude shell/infra from the plan**. Critical: a manifested
     shell file that the filter then drops would trip the manifest-completeness failure at
     `CodeImplementerAgent.cs:237` ("missing files"). The manifest must never list a protected path.
   - Collapse `LegalLinksDoc` into the shell (the footer now lives in `AppShell`), so the AI stops
     authoring legal links entirely.
   - Mirror the reframe into `BatchSystemPrompt`, `SingleShotSystemPrompt`, `RepairSystemPrompt`.

3. **Tests.** `PersonaAgentTests` smoke; a manifest test asserting no protected path is ever
   manifested; `IsProtectedPath` unit tests for the new shell paths; confirm `FilterRepairChanges`
   drops shell edits.

## Sequencing (matches the roadmap's "auth helper → split → Cosmos base")

1. **Agree the path contract** (table above) with Yorrixx — blocks all coding.
2. Yorrixx seeds **`tests/e2e/helpers/auth.ts`** first (smallest proof; already protected by the
   `tests/e2e/` prefix — zero platform code). Validates the approach end-to-end. Confirm the exact
   path: platform protects `tests/e2e/`, but the roadmap wrote `e2e/helpers/auth.ts`.
3. Yorrixx seeds the **frontend shell** (`AppShell` + `routes.tsx` split) ↔ platform ships
   protect+reframe for those paths **in the same release**.
4. Yorrixx seeds the **Cosmos `RepositoryBase`** + **`Program.cs` / `FeatureRegistration` partial**
   ↔ platform protects those paths.
5. Graduate to the `@yorrixx/app-kit` npm package later (roadmap step), once the shell set stabilises.

## Risks on the record

- **Manifest-completeness trap** (change #2) — the highest-risk interaction; the manifest prompt and
  the protected-path list must agree exactly, or builds fail on "missing files."
- **Lockstep release** — protect+reframe cannot precede template seeding. Do not merge the platform
  PR until the matching template PR is ready.
- **`Program.cs` seam** is an open design decision needing Yorrixx buy-in (partial-method registration).
- Unrelated but open: the `AwaitingStageRetry` no-label residual noted in #129/#130.

## Joint ownership

- **Yorrixx:** template content (the actual shell components), `auth.spec.ts` selectors matching the
  shell, provisioning, drift-restore.
- **Platform (this repo):** protected-paths for the shell, the Code Implementer reframe, the manifest
  exclusion, the seam contract.
