# Next-session brief: Scaffold-first + the reconciliation-sweep gap

> **Audience:** a fresh Claude Code session picking up the AI-SDLC platform.
> **Status:** written 2026-06-16 after the **v004 baseline test** (`user-app-ec29e72e`).
> **Scope:** platform = this repo (`ai-sdlc-platform`). Yorrixx-owned items (template seeding,
> `auth.spec.ts`, `verify.yml`, provisioning, Cosmos) live in a **separate session** — coordinate, don't edit.

---

## Why this exists — the v004 baseline finding

v004 was a **fresh app built from scratch with every 2026-06-15 fix live** (#104/#107/#109/#111/#113/#115/#116/#117/#119/#121/#123). It was meant to prove the platform builds a correct app from build #1. Result:

- ✅ **Targeted hardening validated** — no AuthGate / ClerkProvider / typing / risk-gate failures. The analysis pipeline, the Bootstrap risk override (#109), and legal-doc injection all worked.
- 🔴 **Broad first-pass generation is the weak spot.** The 50-file app threw ~15 compile errors and `build-api` + `build-frontend` stayed red through 3 CI-repair attempts. The errors clustered in **regenerated infrastructure**, e.g.:
  - `import … from '@clerk/react'` (hallucinated — should be `@clerk/clerk-react`)
  - `CS0246 'Container' not found` (missing `using Microsoft.Azure.Cosmos;`) in `CosmosRepository.cs`, `ProfileRepository.cs`
  - `CS0246 'IConfiguration' not found` in `ClerkAuthHandler.cs`
  - `CS0246 'IBookingRepository' not found` (referenced, never defined)
  - `TS2339 Property 'rating' does not exist on type 'Booking'` (model/usage mismatch), `TS2503 Cannot find namespace 'NodeJS'`, unused-var TS6133
- 🔴 **The run then *wedged*** — Durable instance stuck `Running` with `lastUpdatedTime` frozen ~21 min on a rate-limited attempt-4 repair model call (50-file gen + 3 repairs = a huge call volume on the current Anthropic tier). **Nothing auto-recovered it** (see the sweep gap below). Instance was manually purged.

**Two conclusions, two workstreams below:** the compile churn + call volume both shrink dramatically if the AI stops regenerating infrastructure → **scaffold-first**. And a heavy build can wedge in a way the platform doesn't self-heal → **sweep gap**.

---

## Workstream 1 — Reconciliation sweep: rescue stuck-`Running` instances (small, do first)

**Problem.** `ReconciliationSweepFunction` only rescues Durable instances in a **`Failed`** terminal state (restarts up to 2× via `reconciliationRestarts`, then `ai-sdlc:reconciliation-exhausted`). v004 wedged in **`Running`** with a stale `lastUpdatedTime` — not `Failed`, so the sweep never touched it.

**Fix.** Extend the sweep to also detect **stuck-`Running`** instances: `runtimeStatus == Running` AND `lastUpdatedTime` older than a threshold (start ~15–20 min; long enough to clear a legitimately slow rate-limited model call + durable retries, short enough to catch a true wedge). Rescue them the same way the `Failed` path does — terminate → purge → restart from the captured input (`showInput=true`) — reusing the existing restart cap so a genuinely-doomed run can't loop forever.

**Watch-outs.**
- Distinguish *slow* from *wedged*: a single rate-limited model call can run ~90s (limiter `MaxDelay`) plus retry backoff. The threshold must sit above the worst legitimate case. Consider also checking that no child activity has completed in the window (the `lastUpdatedTime` signal already captures this).
- Investigate *why* it wedged rather than failing cleanly: an activity exceeding the Functions `functionTimeout` should surface as `Failed` (already handled). The `Running`-frozen state suggests either a hung call or a long durable-timer wait — confirm which, because the right fix might be a per-activity timeout rather than (or in addition to) the sweep change.
- Durable mgmt API used throughout: key = `az functionapp keys list -g rg-aisdlc-dev -n func-aisdlc-dev-81c0 --query systemKeys.durabletask_extension -o tsv`; instance id = `yorrixx-apps_<repo>_<issue#>`; GET `?showInput=true`, POST `/{id}/terminate`, DELETE `/{id}` to purge, POST `orchestrators/AiSdlcWorkflowOrchestrator/{id}` to restart.

**Files.** `src/AiSdlc.Orchestrator/Functions/ReconciliationSweepFunction.cs` (+ its tests in `tests/AiSdlc.Orchestrator.Tests/ReconciliationSweepTests.cs`). Honour the existing `archived:false` org-search qualifier and `MaxRestartableAge`.

---

## Workstream 2 — Scaffold-first (the big lever)

**Thesis.** The platform's job should shrink from *generate the whole app* to *fill feature slots in a fixed frame*. Everything v004 got wrong was **infrastructure**; pin the infra as a tested scaffold and the AI only writes feature pages → far fewer compile errors, far fewer model calls (no rate-limit wedge), faster convergence, lower cost.

### The hard line
| Layer | Who writes it | Examples |
|---|---|---|
| **Platform primitives — FIXED scaffold** | seeded in the template repo, immutable to generation | ClerkProvider + AuthGate (signed-out → Sign up/Sign in modal buttons; signed-in shell w/ `data-testid="signed-in"`), layout + footer + legal links, the Cosmos **repository base/pattern**, the API client (`apiUrl`), the Clerk auth handler, error boundary / loading |
| **App features — AI-generated** | Code Implementer | feature pages (coach search, booking, rating…), domain models, the routes list |

### The critical seam (get this right or files end up half-owned)
- Split `App.tsx` into a **fixed `AppShell`** (ClerkProvider, AuthGate, layout) that renders `<AppRoutes/>`, and an **AI-owned `routes.tsx`** — the *only* place features wire in. Nav items: a generated list the fixed shell consumes.
- **Styling is data-driven** — a `theme.ts` / Clerk `appearance` config the AI may set; it must **never edit the shell components** (that's what protects the immutable `auth.spec.ts` selectors — the entire class of bug we fought on 2026-06-15).
- The Cosmos **data-access** layer was the #1 error source in v004 → strong candidate to pin: a fixed `RepositoryBase`/`Container` wiring + interfaces; the AI writes only typed domain queries (or nothing — feature CRUD against a generic store).

### Enforcement (reuse tonight's machinery)
- Mark the shell paths **platform-owned / immutable to generation** — the same protected-paths pattern as `.github/` and `tests/e2e/` (`AgentActivityFunctions.IsProtectedPath`). The Code Implementer's contract shifts from *describe auth* (fragile — it still got `RedirectToSignIn`/`@clerk/react` wrong) to **"don't touch the shell; fill the feature slots; register routes."**
- **Drift-restore** the shell (Yorrixx-side, like `verify.yml`) as the backstop.
- **First component to extract:** the shared E2E auth helper `e2e/helpers/auth.ts` — the robust `getByRole('button',{name:/sign up/i})` → `.cl-formButtonPrimary` flow that both `auth.spec.ts` and the acceptance helpers import. Kills the flaky homegrown `registerUser` (the open v003 item) and is the smallest proof of the approach.

### Sequencing
1. **Scaffold-first**, not package-first (lowest risk, reuses protected-paths + drift-restore). Graduate to a shared npm package (`@yorrixx/app-kit` on GitHub Packages — you already publish `AiSdlc.Events.Contract` there) once the component set stabilises.
2. Start with **auth helper + the `App.tsx`/`routes.tsx` split**, then the **Cosmos repository base**, then the rest of the shell.

### Joint ownership (coordinate with the Yorrixx session)
- **Yorrixx:** template content (the actual shell components), `auth.spec.ts` selectors must match the shell, provisioning, drift-restore.
- **Platform (this repo):** protected-paths for the shell, the Code Implementer reframe, the seam contract.

---

## Context the new session needs
- The full 2026-06-15 → 06-16 saga is in memory: `project_yorrixx_apps_webhook_wiring.md` (incident-by-incident) and `project_code_implementer_review_pending.md`.
- Open platform issue: **#106** (implementation review approves truncated/incomplete work — deferred).
- Deploy discipline: **never push orchestrator sequence changes while a bootstrap run is mid-flight** (replay break; the sweep rescues but it's wasteful). CI: `TreatWarningsAsErrors` is on. There's an intermittent redaction-regex flake history — bumped to a 2s timeout in #119, re-run failed jobs if it recurs.
- See also `developer-approach.md` (the operating model) and `local-first-future.md` (the fully-local future state).
