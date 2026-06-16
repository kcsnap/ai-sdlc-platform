# The "Developer" approach: scaffold-first code writing — and a fully-local future

> Written 2026-06-16. Companion to `scaffold-first-and-sweep-gap.md`.
> Describes (A) the operating model for how the platform should *write code*, and (B) a
> future-state architecture that runs the loop locally to cut AI-API cost and CI latency.

---

## A. The operating model — scaffold + surgical iteration

Treat the AI like a developer working in an **existing, scaffolded codebase**, not a code printer that
regenerates the world. The whole 2026-06-15/16 effort converged on this; v004 proved that the more the
AI generates from scratch, the more it breaks.

### Principles (each one is hard-won)
1. **Maximise the scaffold.** Auth shell, layout, data-access/repository pattern, API client, Clerk
   wiring, legal pages = fixed, tested, reused. The AI writes only **feature** code on top. Less
   generated surface ⇒ fewer compile errors ⇒ fewer model calls ⇒ faster + cheaper.
2. **Compilation is the only ground truth.** The LLM "implementation review" can't compile, so it must
   never gate a merge — **GitHub Actions `build-api`/`build-frontend` does.** (LLM review stays advisory,
   and is *skipped entirely* on repair runs.)
3. **Rework specific issues only — never full regeneration.** A repair is a **minimal diff** against the
   extracted findings (compiler output / failing checks). Regeneration never converges: each rewrite
   breaks different files (observed repeatedly). Filter repair output to findings-implicated files.
4. **Findings-scoped, not charter-replanned.** On a reopen/repair, pin the whole agent pipeline to the
   findings; don't let the planning agents re-plan the app from the charter (that buried narrow fixes).
5. **Protected paths are sacred.** Never modify `.github/` (CI/CD), `tests/e2e/` verification specs
   (maintain-not-gut for `acceptance.spec.ts`; `auth.spec.ts`/`playwright.config.ts` immutable), or the
   scaffold shell. These are drift-restored by the host.
6. **Bounded convergence.** Repair attempts are capped (currently 4); a no-op/same-SHA repair exits
   honestly rather than burning attempts.

### The loop (and where each step runs)
```
scaffold (fixed shell)                                    ── seeded in template (GitHub)
  → AI generates ONLY feature files            ── Claude API
  → commit to ai/ branch                       ── GitHub (repo)
  → compile: build-api / build-frontend        ── GitHub Actions runners (dotnet build · tsc+vite)
  → red? extract specific findings             ── platform (Azure Durable Functions) + GitHub API
  → AI surgical minimal-diff fix               ── Claude API
  → recommit → recompile  (repeat, bounded)
  → green → auto-merge → deploy                ── GitHub Actions → Azure
  → verify: Playwright vs the deployed app      ── GitHub Actions (verify.yml) vs Azure + Clerk
```
Roles by location: **code gen/fix = Claude API · orchestration/gating = Azure Functions · source =
GitHub · compile/deploy/verify = GitHub Actions · hosting/data = Azure · auth = Clerk.** (See the
architecture mermaid in the session notes / memory.)

### Why scaffold-first compounds
Every error v004 produced was infrastructure (`@clerk/react`, missing `using`s, `CosmosRepository`
`Container`, undefined `IBookingRepository`). Pin that infra and: (a) those errors can't occur, (b) the
generation is small enough not to hit rate limits / wedge, (c) repairs touch a tiny, well-typed surface.
The scaffold is the lever that makes the surgical-iteration loop reliable.

---

## B. Future state — a fully-local fast path (cut AI-API $ and CI latency)

**Motivation.** Two structural costs dominate today:
- **AI APIs are expensive** (a single full build is a large call volume; v004 hit the rate limit and
  *wedged*). Cost scales with generated surface and repair iterations.
- **GitHub Actions is slow** (queueing, runner spin-up, minutes billed) for what is an inner
  edit→compile feedback loop that wants to be seconds.

**Idea: keep GitHub/Azure as the *release* contract, add a LOCAL *inner-loop* path.** Promote to the
cloud path only for the final, verified build. Three local pieces:

1. **Local code-gen / repair models.** Run open/local models (on a VM/GPU box) for generation and —
   especially — the high-volume **repair** calls. Route by difficulty: cheap/mechanical fixes
   (missing `using`, wrong import, unused var, type tweak) → local model; hard design work → Claude.
   The scaffold makes this viable because the remaining surface is small and mechanical.
2. **Local build/compile.** Run `dotnet build` + `tsc`/`vite` on a local runner instead of GitHub
   Actions for the convergence loop — seconds, no queue, no Actions minutes. Two flavours:
   - **Self-hosted GitHub Actions runner** (keeps the exact `ci.yml` contract; lowest divergence), or
   - **a fully local build service** the orchestrator calls directly (fastest; diverges from CI, so
     must reconcile against the real `ci.yml` before release).
3. **Local ephemeral environments.** Containers/VMs to host the app + run Playwright locally during
   iteration, instead of an Azure deploy round-trip per change. Final verification still runs on the
   real deployed Azure app via `verify.yml`.

**Hybrid flow.**
```
inner loop (local, fast, cheap):  local model gen/fix → local compile → local Playwright  → converge
release path (cloud, authoritative): push → GitHub Actions ci.yml (compile) → deploy Azure → verify.yml
```
Only a *converged, locally-green* build is promoted to the cloud path — so the expensive, slow cloud
steps run once per app instead of once per repair iteration.

### Open questions / trade-offs for the new session to weigh
- **Model quality vs cost.** Local code models are weaker → may need more repair iterations, eroding
  the speed/cost win. Mitigated heavily by the scaffold (small, typed surface) and by routing only
  mechanical fixes locally. Measure: iterations-to-green and $/build, local vs cloud.
- **Parity / divergence.** A local build that passes but the cloud `ci.yml` fails is worse than no
  local build. Prefer the **self-hosted-runner** flavour first (same workflow), or add a reconcile
  step. The release path stays the source of truth.
- **Infra cost of "local".** GPUs for local models, VM upkeep, self-hosted runner security/isolation
  (especially for multi-tenant user-app builds). Local isn't free — it trades API/Actions spend for
  capex/ops. Model the crossover point.
- **Secrets & isolation.** Clerk keys, Cosmos creds, signing — handle locally with the same care as
  Key Vault; isolate per-app builds.
- **Determinism / auditability.** The cloud path gives a clean audit trail (Actions logs, App
  Insights). A local path needs equivalent logging to keep the platform debuggable.

### Suggested first experiment
Stand up a **self-hosted GitHub Actions runner** and point a single app's `ci.yml` at it — pure
latency/cost win, zero workflow change, zero quality risk. Separately, prototype routing **mechanical
repair findings** (missing-using / wrong-import class) to a **local model** and compare
iterations-to-green + cost against Claude. Both are low-commitment probes that de-risk the bigger
local-first bet, and both get more valuable once scaffold-first shrinks the surface.
