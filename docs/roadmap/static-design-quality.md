# Static design quality — make marketing pages look designed, not generated

> **Status:** proposed 2026-06-20. Builds on `stack-profiles-static-first.md` (the Static path, now
> proven by user-app-82d06fa5). Goal: generated Tier-1 marketing pages have **modern, themed,
> production-ready design AND copy** — not "clean but basic."
> **Decisions (locked 2026-06-20):** imagery = **generative CSS/SVG now, AI-generated later**; design
> driven by a **dedicated Design Direction step**.

---

## 0. Why the output is basic

The Static contract (#158) is **functional, not aesthetic** — "author `index.html`/`styles.css`,
hard-code data, no backend." Nothing drives a *visual identity* (palette, type, layout rhythm, motion,
imagery, anti-slop), and the **UX agent reviews — it doesn't design.** So the CodeImplementer (a coding
agent) produces correct-but-plain markup. Opus is genuinely strong at design **when given direction**,
so this is a **prompt/role gap, not a model-capability gap.**

`tools/ThemeHarness` (built at the start of this workstream) already does brief → *themed* static site
with a 6-point design rubric. It's the **R&D rig**: iterate the design direction + imagery cheaply
there (and benchmark models on design quality with its cost/time logging), then port the winner.

## 1. The Design Direction step (the core lever)

A **UX/UI Designer** produces a concrete **visual identity** early, derived from the charter/brief, and
the CodeImplementer **builds to it** (like it builds to the architecture spec). Realise by **elevating
`UxAccessibilityReviewerAgent`** from "accessibility review" to "UX/UI design direction + a11y" (it
already runs in the fan-out; fewer moving parts than a new persona). It emits a **Design Direction**:

- **Brand mood** — 2-3 adjectives derived from the vertical/audience/tone (e.g. "calm, clinical, modern").
- **Palette** — primary / accent / neutrals as concrete hex, declared as CSS custom properties on `:root`.
- **Typography** — a deliberate pairing (display + body) via a Google Fonts `<link>`; type scale.
- **Layout & spacing** — grid, section rhythm, a spacing scale, container widths.
- **Components** — buttons, cards, nav, footer styling; border-radius / shadow language.
- **Motion** — tasteful micro-interactions (hover, scroll-reveal), reduced-motion respected.
- **Imagery plan** — §4 (Phase 1: generative CSS/SVG; which sections get which visuals).
- **Anti-slop rules** — no generic Inter/Roboto/system stacks, no purple-on-white gradient cliché, no
  cookie-cutter layout. Distinct, context-specific character. (Leverage the Opus design guidance:
  *propose a concrete direction tailored to the brief, then build it* — don't default to a house style.)

**Wiring requirement:** the Design Direction must reach the CodeImplementer. Today
`CodeImplementerAgent.BuildContextDocs` threads `repoContext / ownerBrief / analystOutput /
architectOutput / implSpec / poReviewFeedback / existingSource` — **not** `uxOutput`. So this needs an
explicit thread of the Design Direction into the implementer's context (a new `Design Direction`
context doc), or the implementer follows nothing design-specific.

## 2. Strengthened Static contract

The Static `ScaffoldContractDoc` (#158) gains design demands: **follow the Design Direction**; build a
modern, responsive, accessible, *polished* page (real spacing/hierarchy, not default-browser); rich
**generative visuals** (gradients/mesh, bespoke inline SVG illustration, an icon set); **real copy, no
placeholder**; the anti-slop rules. It stays static (no React/build/backend).

## 3. Production copy

The Content/BA agents write **real, modern marketing copy** tailored to brand + audience — a proper
hero (headline + subhead + CTA), value props, features/benefits, social proof, and a footer — not
filler. Copy and design are scored together (§5).

## 4. Imagery — Phase 1 now, Phase 2 later

**Phase 1 (now, platform-only): generative CSS + inline SVG.** Self-contained, instant, reliable,
license-clean: CSS gradients/mesh backgrounds, geometric patterns, **bespoke inline SVG illustrations**
and an inline icon set, optional one or two **Google Fonts**. A big jump over "basic" with **zero new
infra** — the implementer authors it directly into `index.html`/`styles.css`. The Design Direction
chooses an illustration/abstract aesthetic suited to the brief.

**Phase 2 (fast-follow, cross-session): AI-generated photographic imagery.** A generate-and-commit step
(image-gen API — e.g. Flux/DALL·E/Stability via Replicate) creates bespoke, on-theme hero/section
images from brief-derived prompts and commits them to `assets/`; the page references them. The designer
picks photo vs illustration per vertical. Needs: image-gen integration + key, a generate-and-commit
step (Yorrixx provisioning or a platform activity), and `assets/` support in the static template.
**Out of scope for Phase 1.** (Stock-photo API → `assets/` is the budget alternative — same infra.)

## 5. Design quality bar

Reuse the **ThemeHarness rubric** as a review/verify check: theme coherence, distinctiveness, content
completeness, layout & responsiveness, visual craft, basic a11y. Apply it (a) in the elevated UX
agent's self-check and (b) as a design-quality signal in review — so a "basic" page is flagged and
fixed, not shipped.

## 6. R&D-first (use ThemeHarness)

Iterate the Design Direction prompt + the static design output **in ThemeHarness** against the rubric —
it's faster and cheaper than full platform runs, and its cost/time logging lets us **benchmark models**
for design quality (a strong case for Opus on the design step even if cheaper models suffice elsewhere).
Port the winning direction into the elevated UX agent + the Static contract. (ThemeHarness needs an API
key to actually render; alternatively iterate directly via real static apps — slower, but "via the app".)

## 7. Platform / Yorrixx split

- **Platform (Phase 1, now):** elevate the UX agent → Design Direction; thread it to the implementer;
  strengthen the Static contract; production-copy demands; design rubric in review. **No Yorrixx change.**
- **Cross-session (Phase 2):** AI-gen/stock imagery → `assets/` (image-gen integration + a
  generate-and-commit step + `assets/` in the static template).

## 8. Sequencing & first step

1. **R&D** the Design Direction + generative-visual prompt in ThemeHarness against the rubric; pick a
   model for the design step.
2. **Platform Phase 1:** elevate `UxAccessibilityReviewerAgent` → Design Direction; thread it into
   `BuildContextDocs`; strengthen the Static `ScaffoldContractDoc`; production-copy demands; rubric check.
3. **Prove** with a new static app — compare design quality vs user-app-82d06fa5 (the "basic" baseline).
4. **Phase 2 (later):** AI-generated imagery (cross-session).

**Proof:** a marketing charter → a static page that is visibly *designed* — coherent theme, real
imagery (generative), modern type/layout, production copy — and still passes render-only verify.

## 9. Favicon + functional forms (shipped) & Web3Forms capture (Yorrixx hand-off)

Ported the ThemeHarness production-polish into the **platform Static contract** (`ScaffoldContract.Static`):

- **Favicon + head** (shipped, no dependency): a bespoke `favicon.svg` brand mark, `<link rel="icon">`,
  `theme-color`, a real `<title>` + meta description. Effective as soon as it deploys — additive files
  the static deploy already serves.
- **Functional forms** (shipped, no dependency): forms validate client-side (labels, types, `required`,
  `preventDefault`, inline errors, aria-live success, reset) — no dead `action="#"`. **Default is
  client-side only** (a static page has no backend), with a conditional hook: *if a Form Capture service
  is supplied, submit to it instead.*

**Web3Forms capture — held, needs Yorrixx provisioning.** The platform consumer is wired and inert: when
`formCaptureEnabled` metadata is `true`, a **Form Capture** posture doc (`AgentContextDocuments`) reaches
every agent instructing the implementer to POST validated forms to `https://api.web3forms.com/submit`.

Key design choice: the implementer writes the **literal placeholder `__WEB3FORMS_ACCESS_KEY__`**, never a
real key — so the secret never travels through the model (prompt redaction can't scrub it) and stays
Yorrixx-owned. **Yorrixx hand-off (the held pieces):**

1. **Provision** a Web3Forms access key (per-app or one shared key — Web3Forms keys are low-risk: they
   only allow sending *you* form mail).
2. **Substitute** `__WEB3FORMS_ACCESS_KEY__` → the real key in the deployed static files (a new step in
   the static `deploy.yml` — the site is otherwise zipped as-is).
3. **Flip** `formCaptureEnabled = true` in the app's charter/metadata so the platform injects the Form
   Capture doc.

Until those ship, static-app forms are **client-side-functional** (validate + success) — a clean,
dependency-free baseline. Open question for Yorrixx: per-app vs shared key, and whether the placeholder
substitution lives in `deploy.yml` or provisioning.
