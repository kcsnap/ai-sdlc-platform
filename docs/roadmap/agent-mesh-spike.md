# Agent mesh — de-risk spike

> Written 2026-06-29. Validates the transport for an autonomous, multi-session dev mesh
> (orchestrator + workers) **without** burning tokens at rest. This is dev-rig tooling that
> *builds* Yorrixx; it is separate from the Yorrixx product's own agent pipeline.

## The one question this spike answers

> Can the orchestrator hand a task to a worker and get the result back **with no human relay
> and no token cost while idle**?

Everything else (more workers, adhoc spawning, parallel fan-out) is known-buildable. The only
unproven assumption is the transport. If this round-trips, the mesh is green-lit on an
architecture that costs nothing at rest. If not, we pivot to a hosted Agent-SDK runner before
investing further.

## Why event-driven, not polling

Separate Claude Code web sessions cannot message each other, and **nothing outside a session
can inject a prompt into it** (Remote Control is UI plumbing, not an API; MCP is pull-only).
The naive fix — keep each worker session awake in a `ScheduleWakeup` poll loop — works but
burns tokens continuously while idle and risks the session being reclaimed mid-wait.

So we drop persistent worker sessions entirely:

```
orchestrator opens a task issue, applies label  agent:worker
        │
        ▼  GitHub webhook  (instant · free · no polling)
GitHub Actions fires → runs Claude headless → does the work → comments the result → exits
        │
        ▼
orchestrator reads the reply comment
```

- **The "monitor" is GitHub's own event system** — nothing to host.
- **The "nudge" is the Actions trigger** (`on: issues.labeled`) — Claude runs *only* when there
  is a task.
- **Idle cost = $0.** Tokens are spent only while work actually happens.
- The session-reclamation risk disappears, because nothing waits.

Your four interactive sessions stay exactly as they are — you talk to them directly whenever
you like. The automated relay rides this Actions path; both operate on the same repo, sharing
state through git.

## The bus convention

**Dispatch (orchestrator → worker):** open an issue and apply the `agent:worker` label.
- Title: `[agent-task] <short description>`
- Body: the full task instruction (workers share no context — put everything they need here, or
  point at a branch/file/SHA).
- Label: `agent:worker` (this is what fires the worker).

**Reply (worker → orchestrator):** the worker posts a comment containing this marker block, which
the orchestrator parses by searching for `<!-- yorrixx:reply`:

```
<!-- yorrixx:reply v=1 -->
### 🤖 Worker reply — <repo>
**Status:** COMPLETE | BLOCKED | FAILED
**Summary:** <one line>
**Details:**
<what was done; links to branch / PR / SHA; anything to sequence next>
```

The orchestrator stays human-paced for now: you open it, it reads outstanding reply comments in
one turn, decides next moves, and fires the next labels. (A reply comment can later trigger an
"orchestrator" workflow too, for a fully hands-off loop.)

## Setup prerequisites

1. **Repo secret `ANTHROPIC_API_KEY`** — add the Anthropic key as an Actions secret
   (`Settings → Secrets and variables → Actions`). It can later be sourced from Key Vault
   (`kv-aisdlc-81c0`) via the existing OIDC login instead of a duplicated secret.
2. **Label `agent:worker`** — create it once: `gh label create agent:worker -c '#5319e7' -d 'Mesh: dispatch to the yorrixx-agents worker'`.
3. **Default-branch caveat** — `issues.labeled` runs the workflow from `main`. The label-driven
   path only goes live once `agent-worker.yml` is merged to `main`. Until then, test with
   `workflow_dispatch`.

## Running the spike

**Branch test (before merge) — proves the Claude round-trip:**
1. Open a trivial, safe task issue, e.g. *"Read CLAUDE.md and report the four `dotnet` commands
   listed under Commands."* (read-only, unambiguous done/not-done).
2. Run the workflow manually: `Actions → Agent Worker (mesh spike) → Run workflow`, passing the
   issue number, with this branch selected.
3. Confirm the worker comments the `yorrixx:reply` block back on the issue.

**Live test (after merge to main) — proves the event-driven trigger:**
1. Open the task issue, apply the `agent:worker` label.
2. Confirm the workflow fires automatically and the reply lands — no manual dispatch, no relay.

## Success criteria

- ✅ Full round-trip with **zero human relay**.
- ✅ Reply arrives in the parseable marker format.
- ✅ The live test fires from the **label event alone** (no manual dispatch).
- 📊 Capture: round-trip **latency** (Action cold-start ~1–2 min), **token cost per task**, and
  confirm **idle cost is zero** (no runs occur between tasks).

## Out of scope (deliberately)

Scaling to 3 workers, adhoc worker spawning, parallel fan-out, a hosted/custom mailbox, retries,
and auth hardening. None of it matters until this round-trip is proven.

## After green

- Add a worker workflow per target repo (`yorrixx-app`, `yorrixx-admin`), each gated on its own
  label (`agent:yorrixx-app`, `agent:yorrixx-admin`).
- Optionally make the orchestrator event-driven (reply comment → orchestrator workflow) for a
  fully hands-off loop.
- Promote the durable design to the Claude Agent SDK if/when it becomes part of the product.
