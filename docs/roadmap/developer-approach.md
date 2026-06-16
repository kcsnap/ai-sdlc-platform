# The "Developer" approach: scaffold-first code writing

> Written 2026-06-16. Companions: `scaffold-first-and-sweep-gap.md` (the design + the sweep gap)
> and `local-first-future.md` (running this loop locally to cut cost + latency).
> Describes the operating model for how the platform should *write code*.

---

## The operating model — scaffold + surgical iteration

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
