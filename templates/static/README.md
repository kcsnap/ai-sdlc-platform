# Static template library

Pre-built, pre-tested marketing-page templates for the **template-first Static build path**. Instead of
generating structure + CSS from scratch on Opus (the dominant cost — see
`docs/roadmap/template-first-static.md`), the platform **selects** a template and **fills its slots** with
a cheap model (Sonnet/Haiku) producing only brand tokens + copy. Assembly is deterministic string
substitution — no LLM writes markup.

Each template directory is a self-contained, drop-in Static app:

```
templates/static/<id>/
  manifest.json          # slot contract + metadata (machine-read by the selector/content steps)
  template.html          # the page, with {{TOKENS}} and <!-- REPEAT:x --> blocks
  styles.css             # layout + motif fixed; brand tokens in :root
  app.js                 # minimal progressive enhancement (page works without it)
  favicon.svg            # tokenised monogram
  acceptance.spec.ts     # known-good, render-only — TEMPLATE-AGNOSTIC (identical across templates)
```

## The slot contract

Three token classes, filled at three different stages — this separation is the whole point:

| Class | Example | Filled by | When |
|---|---|---|---|
| **brandTokens** | `{{BRAND_PRIMARY}}`, `{{FONT_DISPLAY}}`, `{{GOOGLE_FONTS_HREF}}`, `{{BRAND_INITIAL}}` | content model (cheap) | assembly |
| **contentTokens** | `{{HERO_HEADING}}`, `{{ABOUT_BODY}}`, `{{CONTACT_CTA_LABEL}}` | content model (cheap) | assembly |
| **platformTokens** | `{{YEAR}}` | platform (deterministic) | assembly |
| **deployTokens** | `__CONTACT_EMAIL__` | **provisioner/Yorrixx** | **deploy** (never assembly) |

`deployTokens` are deliberately NOT `{{ }}`-shaped so the assembler leaves them untouched and the existing
`EmailLeakGuard` treats them as safe — they're substituted at deploy exactly like `__WEB3FORMS_ACCESS_KEY__`.

### Repeatable blocks

Card/nav lists are delimited and repeated once per array item the content model returns:

```html
<!-- REPEAT:feature -->
<article class="feature-card">…{{ICON}} {{TITLE}} {{BODY}}…</article>
<!-- /REPEAT:feature -->
```

`manifest.json → repeatables` declares each block's token set and a `min`/`max` count.

## The shared structural contract (why the test is template-agnostic)

Every template — whatever its layout — emits the **same structural landmarks**, so one known-good
`acceptance.spec.ts` validates them all and ships verbatim (no per-build test generation, which is what
broke verify in issues #186/#190):

- root `data-testid="app-ready"` on `<main id="main">`
- `data-testid="hero"` with an `<h1>` and `data-testid="hero-cta"` (a `mailto:` link)
- `#features .feature-card` (≥ 3)
- `data-testid="footer-contact"` (a `mailto:` link) + `/privacy-policy.html` + `/terms-of-service.html` links
- contact links use the `__CONTACT_EMAIL__` deploy token; tests assert the `mailto:` **scheme**, never the value

A built-in test (`no unfilled template tokens remain`) fails the build if any `{{ }}` slot was missed — a
free correctness net.

## Content-model output shape

The content step is handed `manifest.json` + the charter/brand brief and returns ONLY this JSON (validated
before assembly). No markup, no CSS:

```json
{
  "brand": { "BRAND_PRIMARY": "#1f4e5f", "BRAND_ACCENT": "#e2734f", "FONT_DISPLAY": "\"Spectral\", serif",
             "GOOGLE_FONTS_HREF": "https://fonts.googleapis.com/css2?family=Spectral…", "BRAND_INITIAL": "T", "…": "…" },
  "content": { "HERO_HEADING": "…", "ABOUT_BODY": "…", "…": "…" },
  "repeat": {
    "nav": [ { "LABEL": "Services", "HREF": "#features" }, … ],
    "feature": [ { "ICON": "◆", "TITLE": "…", "BODY": "…" }, … ]
  }
}
```

## Templates

| id | archetype | best for | moods |
|---|---|---|---|
| `classic-centered` | centered-hero · feature-grid · about · contact | services, clinics, consultancies, B2B | trustworthy, clean, calm |
| `split-feature` | split-hero · alternating-rows · contact-band | studios, agencies, products, hospitality | bold, energetic, editorial |

Add a template by copying a directory, keeping the structural contract + token classes, and registering it
(the selector reads every `manifest.json` under `templates/static/`).
