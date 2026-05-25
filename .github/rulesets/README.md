# Branch policy

Two layers of enforcement keep `main` safe and every branch tied to an open issue.

## Layer 1 — `main` is PR-only (GitHub Ruleset)

`main-pr-required.json` locks `refs/heads/main`:

- Direct pushes are rejected, **including by admins** (`bypass_actors: []`).
- Force-push to `main` is blocked (`non_fast_forward`).
- Deletion of `main` is blocked (`deletion`).
- PRs into `main` must pass two required checks before merge:
  - `build-test` (from `.github/workflows/ci.yml`)
  - `verify-issue-link` (from `.github/workflows/branch-policy.yml`)
- No approving review is required — solo author can self-merge once checks pass.

## Layer 2 — Branch name + issue-link gate (CI workflow)

`.github/workflows/branch-policy.yml` runs on every PR into `main` and fails when:

- the branch name doesn't match `{ai|feat|fix|docs|chore}/{issue#}-{slug}`, or
- the referenced issue number doesn't exist, or
- the referenced issue is not `OPEN`.

Because this workflow is wired as a required status check in Layer 1, a misnamed branch — or one referencing a missing or closed issue — cannot merge into `main`.

> **Why not a push-time ruleset?**
> GitHub's `branch_name_pattern` rule would reject misnamed branches at `git push` time, but it is a **GitHub Enterprise–only** metadata rule and is not available on Free or Pro plans. On this repo, branch naming is therefore enforced at PR-time only. A developer can still *push* a misnamed branch — they just can't merge it.

## Applying the rulesets

```powershell
pwsh .github/rulesets/apply.ps1
```

The script reads every `*.json` file in this directory and is idempotent — for each ruleset, it updates the existing one (matched by `name`) if present, creates it otherwise. To add a new ruleset, drop a JSON file here and re-run.
