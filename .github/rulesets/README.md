# Branch policy

Three layers of enforcement keep every branch tied to a GitHub issue and protect `main`.

## Layer 1 — push-time naming gate (GitHub Ruleset)

`branch-naming.json` defines a repo-level ruleset that rejects pushes of any branch (except `main`) whose name does not match:

```
{ai|feat|fix|docs|chore}/{issue-number}-{kebab-slug}
```

Examples:

- `ai/123-add-delivery-info`
- `fix/42-webhook-retry`
- `docs/9-update-runbook`

## Layer 2 — `main` is PR-only (GitHub Ruleset)

`main-pr-required.json` locks `refs/heads/main` so changes can only land via a merged PR:

- Direct pushes are rejected — including by admins (`bypass_actors: []`).
- Force-push to `main` is blocked (`non_fast_forward`).
- Deletion of `main` is blocked (`deletion`).
- PRs into `main` must pass two required checks before merge:
  - `build-test` (from `.github/workflows/ci.yml`)
  - `verify-issue-link` (from `.github/workflows/branch-policy.yml`)
- No approving review is required — solo author can self-merge once checks pass.

## Applying the rulesets

```powershell
pwsh .github/rulesets/apply.ps1
```

The script reads every `*.json` file in this directory and is idempotent — for each, it updates the existing ruleset (matched by `name`) if present, creates it otherwise. To add a new ruleset, drop a JSON file here and re-run.

## Layer 3 — PR-time issue-link gate (CI workflow)

`.github/workflows/branch-policy.yml` runs on every PR into `main` and fails when:

- the branch name doesn't match the pattern, or
- the referenced issue number doesn't exist, or
- the referenced issue is not `OPEN`.

This catches "valid pattern but fake issue number" cases that the push-time ruleset cannot detect.

## Future: Project status gate

To additionally require the issue to be in the `In Progress` column of a GitHub Project (v2), extend the workflow with a GraphQL query against `projectV2Item -> fieldValueByName(name: "Status")`. Pinning the exact project node ID and status option ID is required; deferred until the Yorrixx project board is set up.
