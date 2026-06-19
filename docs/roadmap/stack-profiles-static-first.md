# Stack profiles — minimal stack by need (Static first)

> **Audience:** the `ai-sdlc-platform` session (this repo) **and** the Yorrixx session (template +
> seeding + wizard + provisioning).
> **Status:** proposed 2026-06-19, from the v011 finding. Companion to `tier-ladder.md` (the ladder this
> realizes) and `conditional-auth-yorrixx-brief.md` (the same charter-driven, system-wide pattern).
> **Decisions (locked 2026-06-19):** profile chosen by **wizard signal + Architect derivation**; start
> with **two profiles, Static vs FullStack** (extend to SPA / +API later).

---

## 0. Why

v011 ("a 1-page marketing site for athletes to search for 121 coaches", all `Needs*` false) was built
as **React + C# Functions + Cosmos**, with a `/api/coaches` Cosmos endpoint for **fixed, public**
content. The `verify` then failed because Cosmos wasn't seeded — chasing data that should never have
left the page. A 1-pager needs **HTML, CSS, and (optionally) a little JS** — not React, not a C# Web API,
not Cosmos.

We proved conditional **auth** (charter `NeedsAuth` → shell variant). This is the same pattern applied
to the **whole stack**: derive the architecture from the requested functionality, default to the least
stack that satisfies it. It realizes `tier-ladder.md` — we have been building **Tier-1 apps on the
Tier-3 full-stack shell**.

**The signal was already there:** v011's charter had `NeedsAuth/Payments/Email/AIApi` all false and no
persistence requirement. The platform just didn't use it to drop the API/DB.

## 1. The rule — add a layer only when a requirement forces it

- **Database** — only if data must **persist or be shared** across sessions/users (user-generated
  content, saved state, admin-editable content that changes post-launch). A fixed list of 5 coaches is
  **hard-coded**, not stored.
- **Backend API** — only if there is **persistence**, a **secret / integration** (send email, payments,
  an AI or 3rd-party API — i.e. any `Needs*` true), or **server-only compute** (a trust boundary,
  private data, heavy compute). "Email the address" is a `mailto:` link, not an endpoint.
- **React / SPA** — only if the UI needs real **client state / routing / interactivity** beyond
  HTML + CSS + light JS. "Search over 5 fixed coaches" is a client-side `filter()`.

By this rule v011 = **Static**: HTML/CSS/JS, no React, no API, no Cosmos.

## 2. StackProfile (start with two)

```
StackProfile = Static | FullStack          // extend later: Spa, FrontendApi, FrontendApiDb
```

- **Static** — `index.html` + `styles.css` (+ optional `app.js`); hard-coded data; static hosting; a
  render-only verify. No React/Vite, no C# API, no Cosmos, no Clerk.
- **FullStack** — today's React + Functions + Cosmos shell (with its auth / no-auth variants from
  `conditional-auth-yorrixx-brief.md` layered on top).

`FullStack` is the binary's "anything that needs a backend" bucket for now; we split it into SPA /
+API / +DB later.

## 3. The seam — the profile is decided **pre-seed** (like NeedsAuth)

The template is scaffolded by Yorrixx **at repo creation**, *before* the platform's Architect runs. So
the Static-vs-FullStack choice that selects the **template** must live in the **charter** at seed time —
it cannot be an after-the-fact Architect switch (you can't turn a seeded React+Functions repo into a
static site). This is exactly how `NeedsAuth` already works.

**Reconciling "wizard signal + Architect derives":**
1. **Wizard captures intent** → a charter signal (§5.1).
2. A **deterministic derivation** (shared rule, §5.2) computes `StackProfile` from the charter
   **pre-seed** → Yorrixx seeds the matching template.
3. The platform **Architect owns the rule's rationale and enforces minimal-stack within the seeded
   profile** — it justifies any API/DB against a named requirement, hard-codes fixed data, and **flags a
   mismatch** (e.g. "this was seeded FullStack but needs nothing server-side") for a future
   re-profiling/escalation path rather than silently over-building.

So the derivation is deterministic where it must be (template selection), and the Architect carries the
judgement and enforcement where it adds value (design rigor, justification, mismatch detection).

## 4. The Static profile shape (what a Static app repo *is*)

| | Static profile | (vs FullStack today) |
|---|---|---|
| Frontend | `index.html`, `styles.css`, optional `app.js` (vanilla) | React + Vite + TS + shadcn |
| Data | **hard-coded** in the page | Cosmos via `RepositoryBase<T>` |
| Backend | **none** | C# Azure Functions API |
| Auth | none | Clerk (auth variant) |
| Deploy | static hosting (e.g. Azure Static Web Apps / storage static site) | Functions + SWA |
| Verify | renders expected content; no scaffold text; links present | Playwright incl. API/Cosmos assertions |
| Immutable shell | minimal (deploy + verify workflows; maybe a `<head>`/meta baseline) | the full scaffold-first shell |

A Static app has almost **no immutable shell** — which makes the variant *lighter* to define than the
full-stack one. (`tools/ThemeHarness` already generates exactly this output shape.)

## 5. Layer-by-layer changes

### 5.1 Charter signal (Yorrixx schema + platform `Charter.cs` mirror)
Add one persistence/static signal the wizard can set — the `Needs*` flags already cover the
integration axis, so the gap is **persistence / dynamic content**. Options (Yorrixx's call):
`Constraints.NeedsPersistence: bool`, or a richer `AppType` / `StackProfile` hint. The wizard asks e.g.
*"Is this a static informational/marketing site, or an app that stores or processes data?"*

### 5.2 Profile derivation (shared, pre-seed)
Deterministic rule, applied where Yorrixx selects the template:
```
StackProfile = Static  if  (no NeedsAuth && no NeedsEmail && no NeedsPayments && no NeedsAIApi
                            && no persistence && no server-compute requirement)
             = FullStack otherwise
```
Mirror it in the platform so agents key off the same value (carried in charter / metadata like
`needsAuth`).

### 5.3 Template family (Yorrixx) — **the long pole**
A **Static template** (or generate-time variant): `index.html`/`styles.css` stub, a static deploy
workflow, a render-only `verify`. Seeding selects by profile, as it does for the auth/no-auth variant.

### 5.4 Architect (platform)
A **minimal-stack posture**: choose the least stack; do **not** introduce an API or DB unless a named
requirement needs persistence / a secret-integration / server compute; hard-code small fixed datasets;
justify any backend; flag profile mismatches.

### 5.5 CodeImplementer (platform)
A **per-profile Scaffold Contract**. The Static contract: "author `index.html` + `styles.css`
(+ optional vanilla `app.js`); hard-code the seed data into the page; **no** React, API, Cosmos, or
fetch to a backend; client-side filtering for any 'search'."

### 5.6 All-agent posture (platform)
A **Static-profile posture** in `AgentContextDocuments.AddStandard` (the same injection point as the
no-auth posture) so the **upstream** agents and spec don't invent an API/DB/React for a static app —
the v011 lesson that conditionalising only the implementer is insufficient.

### 5.7 Spec-gen / DoD / acceptance (Yorrixx + agents)
For Static, the Definition of Done and `acceptance.spec` must be **render-only** (content present,
links work, no scaffold text) — **no** `/api/*` or Cosmos assertions (precisely what failed v011).

## 6. Platform / Yorrixx split

- **Platform (this repo):** the shared derivation mirror; the Architect minimal-stack posture; the
  per-profile Code Implementer contract; the Static all-agent posture; profile carried in metadata.
- **Yorrixx:** the charter signal + wizard question; the **Static template** + static deploy/verify;
  seeding selection by profile; spec-gen/DoD honoring the profile.

## 7. Sequencing & lockstep

Unlike conditional-auth (where the platform posture alone got v011 ~90% there), the Static profile is a
**genuinely different app shape** — the platform agents can't produce a useful static app inside a
React/Functions repo. So the **Static template (5.3) is the trigger**; platform changes pre-stage and
ship with it.

1. Agree this design (platform ↔ Yorrixx).
2. **Yorrixx:** charter signal + wizard question; Static template + static deploy/verify; seeding by
   profile.
3. **Platform:** derivation mirror + Architect posture + per-profile contract + Static all-agent posture
   (draft, held until the Static template is green).
4. Ship together; **proof = a marketing-site charter → a Static repo: `index.html`/`styles.css`,
   hard-coded data, no React/API/Cosmos, render-only verify green.**

## 8. Open sub-decisions (confirm with Yorrixx, non-blocking)
1. Charter signal shape — `NeedsPersistence` bool vs an `AppType`/`StackProfile` field.
2. Static hosting target — Azure Static Web Apps vs storage static website.
3. Whether the Static template carries *any* immutable shell (e.g. a baseline `<head>`/meta, the deploy
   + verify workflows) or is effectively content-only.
