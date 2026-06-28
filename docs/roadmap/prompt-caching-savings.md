# Prompt caching — savings estimate & actuals tracker

> Lever #1 of the "better code, faster" agent work. Caching wired in
> `AnthropicModelProvider` (system prompt + context-document prefix marked
> `cache_control: ephemeral`), default-on, env-killable via `AnthropicPromptCaching=false`.
> This doc holds the **estimate** (analytical, pre-deploy) and the **actuals**
> (computed from telemetry once builds run post-deploy).

## How caching applies here

Every Code Implementer call re-sends a large **stable prefix** — the Scaffold
Contract + charter + business analysis + architecture + UX direction + impl spec
(~8k tokens) — and a small **variable tail** (this batch's files). The prefix is
identical across the build's batch / recovery / CI-repair calls, so it is now
written to cache once and read on every later call.

- **No beta header** needed — `cache_control` is GA on `anthropic-version: 2023-06-01`.
- **Min cacheable prefix on Haiku 4.5 / Opus = 4096 tokens.** The Code Implementer
  prefix clears this comfortably; tiny single-call agents fall under it and simply
  don't cache (no error, no penalty). So the win is concentrated where the spend is.

## Pricing (per 1M tokens)

| Model | Input | Cache read (0.1×) | Cache write (1.25×) | Output |
|---|---|---|---|---|
| Haiku 4.5 (current default) | $1.00 | $0.10 | $1.25 | $5.00 |
| Opus 4.8 (after lever #2) | $5.00 | $0.50 | $6.25 | $25.00 |

## Estimate (analytical)

Assumptions: prefix `P ≈ 8,000` tokens; a typical ~12-file FullStack build runs
`N ≈ 6` prefix-sharing Code Implementer calls (4 batches + ~2 recovery/repair).

Repeated-prefix input cost across the loop:

| | Without cache | With cache | Saved |
|---|---|---|---|
| **Haiku** | 6 × 8k × $1/M = **$0.048** | 8k×$1.25/M + 5×8k×$0.10/M = **$0.014** | **$0.034 (~71%)** |
| **Opus** | 6 × 8k × $5/M = **$0.240** | 8k×$6.25/M + 5×8k×$0.50/M = **$0.070** | **$0.170 (~71%)** |

The fraction saved on the repeated prefix is `(0.9N − 1.15) / N` → ~71% at N=6.
On **Haiku** output dominates the bill, so this is ~10–20% of the implementation
phase in absolute terms. On **Opus** the same 71% applies to a 5× larger number —
this is why caching is the enabler for lever #2. Plus a latency win: cached reads
have lower time-to-first-token.

## Actuals (from telemetry)

`CostEmittingModelProvider` already posts per-call `cacheReadTokens` /
`cacheWriteTokens` (+ `inputTokens`, `outputTokens`) to Yorrixx at
`POST /v1/admin/apps/{appId}/cost`, keyed by `(appId, phase, iteration)`. Realized
savings vs. the no-cache counterfactual, summed over a build:

```
saving_$ = input_rate × (0.9 × cacheReadTokens − 0.25 × cacheWriteTokens)
```

(`input_rate` = $/token for the model used: Haiku 1.0e-6, Opus 5.0e-6.)

Cache-hit ratio (health metric, want it high on the Code Implementer phase):

```
hit_ratio = cacheReadTokens / (cacheReadTokens + cacheWriteTokens + inputTokens)
```

### Actuals log (fill after first post-deploy builds)

| Date | appId | Model | Phase | cacheRead | cacheWrite | input | hit % | saving $ |
|---|---|---|---|---|---|---|---|---|
| _pending deploy_ | | | | | | | | |

> Baseline note: cache fields were always 0 before this change, so there is no
> historical cache data to diff — the rows above start from the first build after
> deploy. Pull them from the Yorrixx cost benchmarking store per `appId`.
