# Template-first Static builds (cost reduction)

**Status:** proposal + starter assets (`templates/static/`). Not yet wired.
**Goal:** remove the dominant build cost by generating Static marketing pages from **pre-built templates +
cheap-model content fill**, instead of authoring structure + CSS from scratch on Opus.

## 1. Why

Cost is concentrated in **one agent on the most expensive model**: the Code Implementer runs on Opus 4.8
(`AnthropicModelOverrides`), in multiple batches/build, each injecting ~24k chars of emitted-file context
(PR #168) and emitting up to 16k output tokens. Estimated **~75–88% of spend**; repair loops re-run it and
multiply it. The Haiku review fan-out is a rounding error by comparison.

For **Static** apps (most builds) the Opus output is almost entirely HTML/CSS *structure* — the part that
varies least between brands (apps differentiate on palette/type/imagery/copy, confirmed empirically). So we
are paying premium per-token prices to re-derive a layout we could template once.

## 2. Approach

For `stackProfile == Static`, replace open-ended code generation with a deterministic pipeline:

```
select template  ──►  fill slots (JSON)  ──►  assemble (string substitution)  ──►  commit
   cheap model         cheap model              NO LLM — deterministic
 (Haiku/Sonnet)       (Sonnet)
```

No LLM writes markup or CSS. The expensive, flaky, hard-to-verify step (free-form code gen) is gone for
Static. FullStack is unchanged (keeps the current Opus path) until/unless templated later.

## 3. Where it slots in

The Static branch already diverges via metadata `stackProfile=Static`
(`AgentContextDocuments.AddStandard` → `StaticProfilePosture`; `ScaffoldContract.Static`). Today the
orchestrator (`AiSdlcWorkflowOrchestrator`) runs the planning agents → Code Implementer → commit loop. The
change: **when `stackProfile == Static` and a template is selected, swap the Code-Implementer stage for the
template pipeline; reuse the existing branch/commit/PR/verify machinery unchanged.**

The pipeline still produces a `IReadOnlyList<FileChange>` (the existing commit contract), so:
- the commit loop, `EmailLeakGuard`, legal-template injection, PR creation, and the verify gate all work as-is;
- the shared `acceptance.spec.ts` ships verbatim from the template (no generated tests → no #190-class failures).

## 4. New components

| Component | Project | Responsibility |
|---|---|---|
| `StaticTemplateLibrary` | `AiSdlc.Agents` (or new `AiSdlc.Templates`) | Load every `templates/static/<id>/manifest.json` + files (embedded resources or content-copied dir). Pure, cached. |
| `TemplateSelectorAgent` (`IAgent`) | `AiSdlc.Agents/Personas` | Cheap model (Haiku). Input: charter/brand brief + template manifests (id, archetype, bestFor, moods). Output: chosen `templateId` + rationale. Deterministic fallback to `classic-centered` if low confidence. |
| `TemplateContentAgent` (`IAgent`) | `AiSdlc.Agents/Personas` | Sonnet. Input: chosen `manifest.json` + brief. Output: the validated slot JSON (brand tokens + content + repeat arrays) — markup-free. Schema-validated; retries on missing/extra tokens. |
| `TemplateAssembler` | `AiSdlc.Shared` | **No LLM.** Apply slot JSON to the template files: `{{TOKEN}}` substitution, `<!-- REPEAT:x -->` block expansion, platform tokens (`{{YEAR}}`), leave deploy tokens (`__CONTACT_EMAIL__`) intact. Returns `List<FileChange>`. Hard-fails if any required slot is unfilled. |

Wiring: register the two agents as `IAgent` singletons (`Program.cs`, alphabetical), add a `Static`
branch in the orchestrator that calls selector → content → assembler instead of `RunCodeImplementerAsync`.

## 5. Slot/assembly spec

Defined in `templates/static/README.md` (the asset contract). Key invariants for the assembler:
- `{{TOKEN}}` → value; unknown/empty required token → **build fails** (caught also by the template's
  "no unfilled tokens remain" acceptance test as a second net).
- `<!-- REPEAT:nav -->…<!-- /REPEAT:nav -->` → repeated once per `repeat.nav[]` item.
- `deployTokens` (`__CONTACT_EMAIL__`) are not `{{ }}`-shaped → never touched at assembly; substituted at
  deploy (Yorrixx) and already guard-safe (`EmailLeakGuard`).
- Output file set is fixed and matches the Static contract: `index.html`, `styles.css`, `app.js`,
  `favicon.svg`, `tests/e2e/specs/acceptance.spec.ts` (+ platform-injected legal pages).

## 6. Cost impact

| | Today (Static) | Template-first |
|---|---|---|
| Structure + CSS | Opus, multi-batch, ~16k out × N | **0 LLM** (template) |
| Brand tokens + copy | (part of the above) | Sonnet, ~1 call, small JSON |
| Template selection | — | Haiku, ~1 tiny call |
| Test generation | Opus (and flaky) | **0** (ships known-good) |

Rough order: a Static build's LLM cost drops from "several large Opus calls" to "one small Sonnet + one tiny
Haiku call" — plausibly **70–90%** off Static, and it removes the verify-failure/repair cost entirely
(repairs were re-running Opus). Validate with real numbers once the Yorrixx cost-read endpoint exists (§9).

## 7. Free vs paid tiering

- **Free:** template-first (Sonnet copy + selected palette/type). Fast, consistent, cheap.
- **Paid (Yorrixx subscription):** unlock bespoke generation — keep the current Opus Code-Implementer path,
  or "template + Opus custom sections," or a larger template set / custom palette reasoning. Cost maps to
  revenue; the selector can route by tier.

## 8. Rollout / fallback

1. Land assets + `TemplateAssembler` + unit tests (assembler is pure → easy to test offline).
2. Add the two agents; gate the whole path behind an env flag (e.g. `StaticTemplateFirst=true`) so it's
   inert until switched on — same pattern as the other config-gated features.
3. **Fallback:** if the selector finds no fitting template (low confidence) or tier is "bespoke", fall back
   to the existing Opus Code-Implementer path. Nothing regresses when off.
4. A/B one batch (template-first vs current) and compare cost + quality + verify pass-rate.

## 9. Dependencies / open questions

- **Measurement:** the cost telemetry (PR #185) is POST-only; add a Yorrixx `GET …/cost` rollup so we can
  prove the saving rather than estimate it. (Separate Yorrixx ask.)
- **Packaging:** embed `templates/static/**` as resources in the agents/templates project, or copy as
  content to the Functions output — decide at implementation (embedded resources travel cleanest).
- **Imagery:** a classified image library (the "paid customisation" half) can layer on later — templates are
  generative-by-default today (`hero-visual` gradient/monogram), so imagery is additive, not blocking.
- **Template growth:** 2 to start; add archetypes (gallery-first, editorial, single-column long-form) as the
  selector earns its keep. Structure variety (held low-priority earlier) comes "for free" as templates grow.

## 10. Touchpoints (for the implementer)

- `src/AiSdlc.Orchestrator/Functions/AiSdlcWorkflowOrchestrator.cs` — Static branch swap (Step ~10/11).
- `src/AiSdlc.Agents/AgentNames.cs`, `Program.cs` — register the two agents.
- `src/AiSdlc.Shared/` — `TemplateAssembler` + `FileChange` reuse.
- `src/AiSdlc.Agents/AgentContextDocuments.cs` — Static posture already gates on `stackProfile`.
- `templates/static/**` — the asset library (this change).
- Tests: `TemplateAssembler` unit tests (substitution, repeat expansion, unfilled-token failure, deploy-token
  preservation); selector/content smoke tests with `FakeModelProvider`.
