# Contributing

## Issue-first workflow

Every change starts with a GitHub issue. There are no exceptions — the merge-time gate (`verify-issue-link` in `.github/workflows/branch-policy.yml`) rejects PRs whose branch doesn't reference an existing, `OPEN` issue.

1. **Open or pick an issue** on [github.com/kcsnap/ai-sdlc-platform/issues](https://github.com/kcsnap/ai-sdlc-platform/issues).
2. On the issue page, click **"Create a branch"** (in the right sidebar under *Development*). GitHub auto-prefills a correctly-named branch like `24-update-contributing`. Adjust the prefix to one of `ai|feat|fix|docs|chore` and slug as needed, then create.
3. Check out the branch locally and start work.

This UI shortcut is the easiest way to stay in policy. You can also create the branch manually — just follow the naming convention below.

## Branch naming

```
{ai|feat|fix|docs|chore}/{issue-number}-{kebab-slug}
```

Examples:

- `ai/123-add-delivery-info`
- `feat/42-waitlist-form`
- `fix/9-webhook-retry`
- `docs/24-update-contributing`
- `chore/22-gitignore-artifacts`

What the `verify-issue-link` workflow checks on every PR into `main`:

- Branch name matches the pattern above.
- The referenced issue number exists in this repo.
- The referenced issue is in `OPEN` state (closed issues are rejected).

## Working rules

- Keep each slice small and focused.
- Open a PR for every change — **direct pushes to `main` are blocked** at the server level by the `main-pr-required` ruleset (no admin bypass).
- Run `dotnet build AiSdlc.sln` locally before opening a PR. The build treats all warnings as errors.
- Run `dotnet test AiSdlc.sln` locally before opening a PR.
- Add or update tests with every code change.
- Do not implement live external integrations until the interface and tests are stable.
- Prefer deterministic code before AI/model behaviour.
- Keep provider-specific code behind abstractions.
- Keep secrets out of prompts, tests, source files, and logs.

## Commit messages

Short imperative subject line in conventional-commit form: `type(scope): summary`. Add a blank line and a body when the reason isn't obvious from the diff.

Examples from history:

- `feat(ci): lock main to PR-only + enforce branch-name/issue-link policy`
- `fix(risk-assessor): scope keyword detection to relevant markdown sections`
- `chore: ignore build and runtime artifacts`

## Pull requests

- Title: short imperative phrase, under 70 characters.
- Body: what changed and why. Include `Closes #N` so the linked issue auto-closes on merge.
- All required CI checks must pass before the merge button enables:
  - **`build-test`** — restore, build (warnings = errors), test the full solution.
  - **`verify-issue-link`** — branch name and linked-issue check (see above).

## Ruleset enforcement

`.github/rulesets/main-pr-required.json` is the authoritative source for what's blocked on `main`. Currently:

- Direct push: rejected (no admin bypass).
- Force-push: blocked (`non_fast_forward`).
- Deletion: blocked.
- Merge requires both required checks to pass.
- No approving review required (solo author can self-merge).

See [.github/rulesets/README.md](.github/rulesets/README.md) for the full ruleset story, including why `branch_name_pattern` push-time enforcement isn't available on Free plan.

## Local setup

See [docs/local-development.md](docs/local-development.md).
