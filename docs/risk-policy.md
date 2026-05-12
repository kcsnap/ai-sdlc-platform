# Risk Policy

## Overview

The AI SDLC Platform uses a two-layer risk assessment:

1. **Deterministic rules** (`RiskRulesEngine`) — fast, repeatable scoring based on changed file patterns. No AI.
2. **AI synthesis** (`RiskAssessorAgent`) — reads all specialist review outputs and produces a final risk decision with rationale.

Both layers must agree before a change is eligible for autonomous merge.

---

## Risk levels

| Level | Meaning | Default action |
|-------|---------|----------------|
| Low | Routine change with no high-risk signals | Auto-merge eligible |
| Medium | Non-trivial change; review required | Human review recommended |
| High | Change touches auth, payments, PII, secrets, or infra | Human review required |
| Unknown | Insufficient signal to classify | Block; treat as high |

---

## Deterministic rules (RiskRulesEngine)

Applied based on changed file paths and metadata flags.

### Low risk signals

| Signal | Example paths |
|--------|---------------|
| Docs-only change | `docs/**`, `*.md` |
| Tests-only change | `tests/**`, `*.Tests/**` |
| Simple frontend/content | `src/frontend/public/**` |

### Medium risk signals

| Signal | Example paths |
|--------|---------------|
| API surface change | `src/api/Controllers/**` |
| Database migration | `migrations/**`, `*.sql` |
| Terraform change | `infra/terraform/**` |
| GitHub Actions workflow | `.github/workflows/**` |

### High risk signals

| Signal | Example paths |
|--------|---------------|
| Authentication / authorisation | `src/api/Auth/**`, `*Middleware*` |
| Payment / checkout | `src/api/Payments/**`, `src/frontend/*/Checkout*` |
| Personal data handling | files flagged via `personalDataHandlingChanged: true` |
| Secrets / Key Vault | `*keyvault*`, `*secret*`, `*.pfx` |

### Blocking conditions

- Any mandatory quality gate fails (CI checks, required reviews)
- Unknown/ambiguous signal set (no paths matched, no flags set)

---

## AI risk assessment (RiskAssessorAgent)

After all specialist reviews complete, the `RiskAssessorAgent` receives a consolidated context of all review outputs and produces one of three decisions:

| Decision | Meaning |
|----------|---------|
| `AUTO_MERGE_ELIGIBLE` | All reviews green; deterministic rules confirm low risk |
| `HUMAN_REVIEW_REQUIRED` | One or more reviewers raised concerns requiring human judgement |
| `BLOCKED` | Critical issue found; workflow must not proceed |

The agent also outputs a risk level (Low/Medium/High), a rationale, and specific flagged concerns.

---

## Auto-merge eligibility gates

All ten gates must pass for autonomous merge:

1. Risk level is `Low`
2. AI risk decision is `AUTO_MERGE_ELIGIBLE`
3. Product Owner approved the refined brief
4. All mandatory reviews completed
5. No blocking issues reported by any reviewer
6. All CI checks pass
7. Test coverage present
8. Rollback plan documented
9. Release notes generated
10. Post-deployment checks defined

If any gate fails, the platform posts the failed gates to the PR and routes to human review.

---

## Human review routing

When `HUMAN_REVIEW_REQUIRED` is returned, the orchestrator waits for an `ApproveRelease` external event (14-day timeout). The Release Manager's output tells reviewers exactly what they are approving.

If the timeout expires without approval, the workflow enters `Failed` state and the issue is labelled `ai-sdlc:timed-out`.

---

## Configuring risk thresholds

Application repositories can set `risk.auto_merge_threshold` in `.ai-sdlc.yml`. The default is `low`; setting to `none` disables autonomous merge entirely for that repo.

Configurable thresholds per signal type are planned for a future release.
