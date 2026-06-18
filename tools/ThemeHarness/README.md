# ThemeHarness — Tier-1 (pure-visual) generation harness

A standalone dev tool that proves the platform can turn a **customer brief** into a
**purely-visual, themed marketing site** (static HTML + CSS, no backend, no functionality)
— Tier 1 of the staged complexity ladder (see `docs/roadmap/tier-ladder.md`).

It deliberately reuses the **real** `AnthropicModelProvider` (prompt redaction + rate
limiting) the platform uses, so we're testing the actual provider path — only the prompt
and the output shape are Tier-1-specific. It is **not** part of `AiSdlc.sln`/CI.

## Why this exists

The v004 baseline showed broad full-stack generation is the weak spot. Before re-investing
in the production pipeline + template plumbing for a new tier, we want fast, cheap signal on
the one thing that actually matters at Tier 1: **can the AI produce several distinct,
high-quality themed UIs?** This harness gives that signal in one session, with no
Yorrixx-side template or provisioning changes.

## Usage

```powershell
# from this folder
$env:AnthropicApiKey = "<your key>"      # same key the platform uses

dotnet run -- list                        # show the built-in customer briefs
dotnet run -- generate brightsmile-dental # generate one site
dotnet run -- generate all                # generate every brief (sequential)
dotnet run -- serve brightsmile-dental    # preview at https://localhost:5443

# benchmark spend/time/tokens across models on the same brief(s)
dotnet run -- benchmark brightsmile-dental --models claude-opus-4-8,claude-sonnet-4-6,claude-haiku-4-5
dotnet run -- serve brightsmile-dental --model claude-opus-4-8   # preview one model's output
```

Options: `--model <id>` (default `claude-sonnet-4-6`), `--max-tokens <n>` (default 16000),
`--port <n>` (serve, default 5443), `--models a,b,c` (benchmark).

Generated sites land in `output/<slug>/` (single-model) or `output/<slug>/<model>/` (benchmark),
all git-ignored. First HTTPS preview may need `dotnet dev-certs https --trust` once.

## Benchmarking

`benchmark` runs each brief through each model sequentially, writes each model's site to its own
subdir (for side-by-side eyeballing), and appends a row to `output/benchmark-results.csv`:
`timestamp, slug, model, input_tokens, output_tokens, cost_usd, seconds, truncated, files, ok, error`.
Cost is computed from per-model pricing in `Pricing.cs` (no prompt caching is used, so it's a flat
input×rate + output×rate). This covers the *comparison* half of benchmarking; production *spend
measurement* lives in the platform audit/dashboard (see `docs/roadmap/tier-ladder.md`).

## The scoring rubric (Tier-1 "done" bar)

Pass = your sign-off on **≥3 distinct** themes clearing all six:

1. **Theme coherence** — palette/type/imagery match the brief's vertical
2. **Distinctiveness** — visibly different from the other themes (not one layout recoloured)
3. **Content completeness** — every section in the brief is present, with real copy
4. **Layout & responsiveness** — holds up mobile + desktop
5. **Visual craft** — spacing, hierarchy, polish; not "generic template"
6. **Basic accessibility** — contrast, semantic HTML, focus states, alt/aria on SVG

## What's intentionally NOT here

No API, no database, no auth, no email, no JS framework — those are Tiers 2-6, each its own
separate solution. Keeping Tier 1 pure is the point.
