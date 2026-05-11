# Contributing

## Working rules

- Keep each implementation slice small and focused.
- Create one branch per slice (`feature/`, `fix/`, `ai/` prefixes are fine).
- Open a PR for every meaningful change — no direct pushes to `main`.
- Ensure `dotnet build` passes before opening a PR.
- Ensure `dotnet test` passes before opening a PR.
- Add or update tests with every code change.
- Do not implement live external integrations until the interface and tests are stable.
- Prefer deterministic code before AI/model behaviour.
- Keep provider-specific code behind abstractions.
- Keep secrets out of prompts, tests, source files, and logs.

## Branch naming

```
ai/NNN-short-description        AI SDLC platform slices
feature/short-description       General feature work
fix/short-description           Bug fixes
docs/short-description          Documentation only
```

## Commit messages

Use a short imperative subject line. Add a blank line and a body if the reason is not obvious from the diff.

## Pull requests

- Title: short imperative phrase (under 70 characters).
- Body: what changed and why. Link the relevant TODO section.
- All CI checks must pass before merge.

## Local setup

See [docs/local-development.md](docs/local-development.md).
