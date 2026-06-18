# The complexity ladder — proving the platform one layer at a time

> **Status:** started 2026-06-18. Supersedes the "build the whole app at once" assumption that
> the v004 baseline showed to be the weak spot (see `scaffold-first-and-sweep-gap.md`).
> **Owner:** platform session (this repo). Template/provisioning items are Yorrixx-owned.

## Why

We went too complex too soon — API + database + Clerk + email + payments all at once. What we
built was good, but complexity outran our confidence in the simple foundations. v004 confirmed it:
targeted hardening worked, **broad first-pass generation didn't**, and everything that broke was
infrastructure. So we prove the platform on a **ladder**, one capability per rung, and we **stay on
a rung until we're comfortable** before climbing.

Each rung is a **completely separate solution** — we do not build one app up across the rungs. The
point is repeatable proof, not a single growing codebase.

## The rungs

| Tier | Adds | Capability proven |
|---|---|---|
| **1** | Themed marketing UI, pure HTML/CSS, **no functionality** | Visual/UX quality + theme variety from a brief |
| **2** | A form: two integers → API → sum → display | Frontend ↔ API round-trip; **dev→build iteration** (below) |
| **3** | + persistence: save inputs/outputs, show full history | Database wiring (the #1 v004 error source) |
| **4** | + email provider on each calculation | Outbound third-party integration |
| **5** | + Clerk registration required to use the sum | Auth / identity |
| **6** | + multiple pages, different calculations | Routing / multi-feature composition |

**Tier 1 is the priority.** We don't touch Tier 2 mechanics until Tier 1 is comfortably proven
across several distinct themes.

## Locked decisions (2026-06-18)

- **Tier-1 stack:** static HTML/CSS (no framework, no build). Literal interpretation of "purely visual."
- **Mechanism:** a **minimal template/profile per tier** — each tier is its own seed, not a constrained
  full-stack app. (For Tier 1 we proved theme quality with a standalone harness *first* — see below.)
- **Customer briefs:** the platform session drafts varied sample briefs (5 to start); real briefs can
  swap in later.
- **Tier-1 "done" bar:** a lightweight rubric **+ the user's visual sign-off** on **≥3 distinct themes**.

### Tier-1 rubric

1. Theme coherence · 2. Distinctiveness · 3. Content completeness · 4. Layout & responsiveness ·
5. Visual craft · 6. Basic accessibility. (Full wording in `tools/ThemeHarness/README.md`.)

## How Tier 1 is being driven — standalone harness first

Rather than re-plumb the production pipeline + a new template repo for Tier 1, we built
`tools/ThemeHarness` (this repo): **brief in → themed static site out → preview locally over HTTPS.**
It reuses the real `AnthropicModelProvider` (redaction + rate limiting), so it tests the actual
provider path; only the prompt (`ThemePrompt.cs`) and output shape are Tier-1-specific. This gives
fast, cheap signal on theme quality with **no Yorrixx-side changes**. Once the output convinces us,
*then* we wire Tier 1 into the real pipeline + a minimal static template.

The five seed briefs span clinical (dental), warm-artisanal (coffee), dark-tech (SaaS),
formal-traditional (law), and calm-organic (yoga) — chosen to stress theme range.

## The platform / Yorrixx boundary (carry into every tier)

Per `scaffold-first-platform-plan.md` §7: **template repos, the `EnsureUserAppRepoAsync` seeding
switch, provisioning, deploy/verify workflows are Yorrixx-owned** (separate session). This session
owns the Code Implementer contract, protected paths, the pipeline, and the risk engine. The
"minimal template per tier" mechanism therefore needs cross-session coordination for every rung
*except* the standalone-harness prototyping we do first.

## Open thread to resolve at Tier 2 — the develop→build iteration loop

**Concern (user, 2026-06-18):** by Tier 6 we must **not** let the AI write thousands of lines with no
build validation in between — exactly the v004 failure mode (50-file gen, ~15 compile errors, 3 failed
repair rounds, then a rate-limit wedge). We need an incremental **design → build → validate** loop
baked into generation, growing in sophistication as the tiers do.

**To discuss / prototype at Tier 2 (simplest case), then grow:**
- **Contracts/interfaces first** — generate the interfaces and types with `NotImplementedException`
  bodies; **compile that skeleton** before any logic exists. A failing build here is cheap.
- **TDD per method** — write the test for a method, then fill the method, build + run that test, move
  on. Each step is a small, independently-validated diff instead of one giant unvalidated dump.
- **Build gates between steps** — the generator checkpoints against a real compile/test, not just at
  the very end, so errors are caught one-at-a-time while they're cheap to fix and cheap on call volume.
- **Tie-in to scaffold-first:** the more infra is pinned in the template, the less the AI generates,
  the fewer build cycles needed — these are complementary, not competing.

This is the through-line that makes Tiers 2-6 safe. Tier 1 (no code, no build) deliberately sidesteps
it — which is part of why it's first.

## The blocker for "Tier 1 via the real app" — auth must be conditional (2026-06-18)

We pivoted from the standalone harness to driving Tier 1 **through the real Yorrixx app**. That
immediately surfaced the real blocker: **Clerk is hard-wired into every app, so a pure marketing page
fails immediately** (a sign-in wall in front of a no-functionality site).

**Finding (good news — smaller than it looks):** the discovery wizard's answer already exists in the
data model. `Charter.Constraints.NeedsAuth` (+ `NeedsEmail`, `NeedsPayments`, `NeedsAIApi`) is in
`AiSdlc.RepoIndex/Charter/Charter.cs` and is even rendered into the charter markdown agents read
(`CharterMarkdownRenderer.cs`). But the scaffold **ignores it**: `ScaffoldContractDoc`
(`CodeImplementerAgent.cs`) is a single const string assigned to **every** app
(`docs[ScaffoldContractLabel] = ScaffoldContractDoc` — "applies to every user-app") that hard-states
*"AUTHENTICATION IS ALREADY DONE … main.tsx wraps the app in `<ClerkProvider>`"*. So the AI is shown
"Needs auth: No" and simultaneously told auth is mandatory — and the immutable shell physically ships
ClerkProvider, so the shell wins.

**The principle:** the shell should be **composed from the charter's capability flags, not fixed**.
Auth is just the first/most glaring case (it hard-fails Tier 1); the same is true of Cosmos/DB (a Tier-1
page ships an unused DB), email (Tier 4), and payments. Tiers map to flag combinations: Tier 1 = all
false → barest shell; Tier 3 = +DB; Tier 4 = +email; Tier 5 = +auth.

**Decision (2026-06-18): auth-first, then DB/email per tier.** Smallest safe step that unblocks Tier 1,
extended as later tiers need it.

**This revises the locked 2026-06-16 contract** (`scaffold-first-platform-plan.md` §2 assumes Clerk is
in the immutable shell unconditionally), so it needs cross-session coordination:
- **Platform (this repo):** make `ScaffoldContractDoc` + protected-paths **conditional on
  `Charter.Constraints`** — when `NeedsAuth=false`, don't claim auth is wired, don't protect Clerk
  paths, and adjust `acceptance.spec` expectations (no `signed-in` gating). Hold behind the lockstep
  gate (don't merge ahead of the template/seeding support — same discipline as §1 of the locked plan).
- **Yorrixx (separate session — the convo):** the template shell needs an **auth / no-auth variant**
  (or one conditional shell); the seeding switch picks it from `NeedsAuth`; drift-restore, `auth.spec`
  presence, and the deploy Clerk env all branch.

The full hand-off (no-auth path contract, lockstep gate, acceptance test, open decisions) is in
[`conditional-auth-yorrixx-brief.md`](./conditional-auth-yorrixx-brief.md).

## Benchmarking model choice (decision: both, 2026-06-18)

We want to choose the model on **spend + time + quality** evidence, not by default. Two halves:
- **Measurement (production spend/time):** belongs in the **platform audit + dashboard** — record model
  id, input/output tokens, latency, and computed cost per agent call. Fits "via the app" with no key
  handling. `ModelResponse.Usage` already carries token counts; this is the natural extension. *(Not
  built yet — next platform chunk.)*
- **Comparison (same input, swap models):** lives in the **`tools/ThemeHarness` benchmark rig** — fast,
  isolated A/B on identical briefs, logs `benchmark-results.csv` (tokens/cost/seconds/truncation) and
  writes each model's output to its own subdir for side-by-side eyeballing. Pricing is sourced from the
  `claude-api` skill model table (`Pricing.cs`). **Built.** Run:
  `benchmark <slug|all> --models claude-opus-4-8,claude-sonnet-4-6,claude-haiku-4-5`.

Default single-model is `claude-sonnet-4-6`; visual quality is the biggest Tier-1 lever, so Opus is
worth a comparison run.
