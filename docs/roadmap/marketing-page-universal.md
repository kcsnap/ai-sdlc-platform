# Marketing page universal — every app ships a public marketing 1-pager

> **Status:** proposed 2026-06-21. Yorrixx hand-off brief (shell change required) + held platform
> pre-stage. **Decision (locked 2026-06-21):** a themed marketing 1-pager is a **universal, mandatory
> component of every generated app** — a FullStack app (Clerk + dashboard) ships a marketing landing
> **in addition to** the authed product, **never instead of** it. The complexity profile decides what's
> *behind* the door, not whether a public front door exists.

---

## 0. Why

Static apps already *are* a marketing page. But a FullStack app today has no public front door — a
signed-out visitor sees only the Clerk modal's "Sign up" / "Sign in" buttons. Real SaaS needs a public
marketing landing that sells the product, with sign-up/sign-in as CTAs. With the Design Direction step
now live (UX agent → bespoke visual identity), we can make that landing genuinely designed, not plain.

## 1. Current shell behaviour (the gap)

Per the FullStack Scaffold Contract: `main.tsx` wraps `<ClerkProvider>`; `app/AppShell.tsx` renders the
auth gate — **signed-out shows the Clerk modal "Sign up" / "Sign in" buttons** (the immutable
`auth.spec.ts` asserts this), **signed-in renders the dashboard** (`routes.tsx` `AppRoutes` inside the
signed-in layout, marked `data-testid="signed-in"`). There is **no public route** outside the auth gate
for a marketing page to live in.

## 2. The contract: the marketing landing *is* the signed-out view

The clean design that **keeps `auth.spec.ts` green**: the signed-out view becomes the **marketing
landing**, and that landing **embeds the Clerk "Sign up" / "Sign in" affordances** as its CTAs (hero
"Get started" → Clerk sign-up; nav "Sign in" → Clerk sign-in). So:

- **Signed-out** → themed marketing 1-pager (hero, value props, features, social proof, footer) whose
  primary CTAs are the existing Clerk sign-up / sign-in buttons. `auth.spec.ts` still finds them.
- **Signed-in** → today's dashboard, unchanged.
- The landing is a **feature slot the implementer fills** (e.g. `src/frontend/src/features/marketing/`),
  built to the **Design Direction** (palette/type/motif via the styling seam). It is public — no API
  calls, no auth-gated data; hard-coded marketing copy + generative visuals (same quality bar as the
  Static path).
- **Static apps:** already a marketing page — **no change** (the whole app is the landing).

## 3. Platform / Yorrixx split

**Yorrixx (shell — the load-bearing change):**
1. `AppShell.tsx` signed-out branch renders a **marketing landing slot** (e.g. imports
   `features/marketing/Landing`) **instead of** a bare Clerk-button screen — with the Clerk sign-up /
   sign-in buttons embedded **inside** that landing so `auth.spec.ts` keeps passing.
2. Define the **slot path + export contract** the implementer authors against (name, props, where the
   Clerk buttons are injected vs. owned by the slot).
3. Acceptance/verify covers the public landing render (real content, scaffold marker gone) in addition
   to auth + dashboard.

**Platform (held until the slot ships — conditional-auth lesson: don't wire ahead of the shell):**
1. FullStack `ScaffoldContractDoc` gains a **"Public marketing landing"** slot section: author
   `features/marketing/**` to the Design Direction; public-only (no API/auth-gated data); embed the
   Clerk CTAs per the shell contract; real copy + generative visuals.
2. Planning agents (Architect / PO) treat the marketing landing as a standard deliverable for every
   FullStack app (not optional).
3. The Design Direction (UX agent) already covers it — the landing is exactly where it lands hardest.

## 4. Acceptance / proof

A FullStack charter → an app where: signed-out renders a **designed marketing landing** (themed, real
copy, generative visuals) with working **Get started / Sign in** CTAs into Clerk; signing up reaches the
**dashboard**; `auth.spec.ts` + the dashboard acceptance specs stay green; a new **public-landing render
check** passes. Compare design quality against the Static marketing baseline.

## 5. Open questions for Yorrixx

- **Slot shape:** exact path + export (e.g. `features/marketing/Landing.tsx` default export) the shell
  imports for the signed-out view?
- **Clerk affordances:** does the shell inject the Clerk buttons into the landing (slot renders a
  placeholder), or does the landing own the buttons by importing Clerk components? (Former keeps the
  landing Clerk-agnostic; latter gives the designer full layout control of the CTAs.)
- **auth.spec.ts:** does it stay byte-identical (buttons present anywhere in the signed-out DOM), or
  does it need a tweak to find buttons inside the landing?
- **SEO/meta:** is the public landing pre-rendered/SSG at all, or client-rendered SPA (affects meta,
  social cards, crawlability)? Likely out of scope for v1 — confirm.

## 6. Sequencing

1. **This brief** → Yorrixx confirms the slot shape (§5).
2. **Yorrixx** ships the shell change (signed-out → marketing slot with embedded Clerk CTAs) + verify.
3. **Platform** un-holds the contract slot + planning-agent requirement (held pre-stage).
4. **Prove** end-to-end; compare to the Static marketing baseline.

---

## Relationship to other roadmap docs

- `static-design-quality.md` / the Design Direction step (UX agent) — the marketing landing is where the
  bespoke visual identity matters most; same quality bar as the Static path.
- `stack-profiles-static-first.md` — Static vs FullStack decides what's *behind* the door; this brief
  establishes that a public marketing door exists for **both**.
- `fullstack-capability-derivation.md` — the landing is public/stateless regardless of the resolved
  capability profile.
