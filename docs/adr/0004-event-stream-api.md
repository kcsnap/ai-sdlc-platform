# ADR 0004: Platform → Yorrixx Event-Stream API Contract

## Status

Accepted (2026-06-02)

## Context

The `kcsnap/ai-sdlc-platform` orchestrator runs an AI SDLC pipeline against a GitHub issue and emits ~50 distinct event types (webhook receipts, agent lifecycle, comment posts, workflow terminal states) to Azure Table Storage via `IAuditService`. The Yorrixx web app (`kcsnap/yorrixx-app`) needs to surface per-run progress to end users in near-real-time.

Today there is no published API for this. As an interim measure, PR #51 added invisible HTML-comment **terminal-status markers** (`<!-- ai-sdlc:status=completed -->` / `<!-- ai-sdlc:status=failed -->`) to Bootstrap-mode runs that Yorrixx can scrape from GitHub PR comments. This is explicitly a stop-gap. The platform's locked architectural commitment to Yorrixx (architecture-decision #9) is to expose:

> A versioned event-stream API: `GET /runs/{runId}/events?since={cursor}` returning paginated, monotonic-cursor events typed against a published `AiSdlc.Events.Contract` package. Yorrixx depends on the contract, not on `AuditEvents` table internals.

The audit subsystem already operates as an event stream in everything but name. The current surface (mapped 2026-06-02):

| Property | Current state |
|---|---|
| Storage | Azure Table Storage `AuditEvents`. `PartitionKey = RunId` (`{owner}_{repo}_{issueNumber}`). `RowKey = {UtcTicks:D20}_{Guid:N}` — chronologically sortable, collision-free. |
| Query | `IAuditService.GetSinceAsync(DateTimeOffset since, int maxResults)` returns events oldest-first, post-fetch sorted. |
| Event taxonomy (de facto) | 4 actor types × N actions ≈ 50 events. ActorTypes: `Webhook`, `Agent`, `Comment`, `Workflow`. |
| Run identity | `RunId = {owner}_{repo}_{issueNumber}` — stable, derived at webhook arrival, used as Durable Functions instance ID, never randomised. |
| Contract package | None. `AuditEvent` is the de facto DTO; `AiSdlc.Shared` is not published. |
| HTTP surface | One Function: `POST /github/webhook` (signature-validated). No read API. |
| Dashboard consumption | `AiSdlc.Dashboard` polls `GetSinceAsync` at 1–5 s intervals against a monotonic high-water mark — already the consumption pattern proposed for the API. |

This ADR formalizes the contract. Implementation lands in follow-up PRs.

## Decision

### Route and version

```
GET /v1/runs/{runId}/events?since={cursor}&limit={n}
```

URL-path versioning. Reserves `/v2/...` for future breaking changes; both versions can run in parallel during migration.

### Hosting (v1)

The endpoint lives in the **existing `AiSdlc.Orchestrator` Functions host** alongside `GitHubWebhookFunction`. Single deployment, shares DI + `IAuditService` directly.

**Designed for clean future split:** the URL path, auth model, response shape, and `IAuditService` boundary are chosen so that lifting the endpoint into a new `AiSdlc.Events.Api` Function App later is an infrastructure change only — no client-visible impact. Split is a v2 trigger when read traffic or failure-isolation concerns warrant.

### Auth (v1)

**Function-level API key** (`AuthorizationLevel.Function`). One shared key, injected via Function App config (`EventsApiKey`). Yorrixx sends `x-functions-key: {key}` (or `?code={key}` for compatibility).

Rotation: update Function App config + Yorrixx env var. No code change. No contract change.

Future upgrade path (out of scope for v1): swap to **OIDC / Microsoft Entra** with per-tenant scoping. The contract is unaffected — only the auth header changes.

### RunId

`{owner}_{repo}_{issueNumber}` — exposed verbatim as the route segment. Already what Yorrixx sees in PR URLs and GitHub references. Case-sensitive (matches Table Storage PartitionKey semantics).

Multi-tenant opacification (when Yorrixx hosts multiple orgs and per-tenant RunIds need to be opaque) is a separate future ADR.

### Cursor

**Opaque base64-URL-encoded token** derived from the AuditEvents `RowKey`. Clients treat it as opaque.

- `since=` absent or empty → return events from the beginning of the run.
- `since=<cursor>` → return events strictly after this cursor, ordered oldest-first.

The opacity is the load-bearing property: it lets the platform swap storage backing (Table → Cosmos change feed) without a contract break.

### Pagination

```
GET /v1/runs/{runId}/events?since=<cursor>&limit=<n>
```

- `limit` default **100**, max **500**. Out-of-range values clamp silently to the bounds.
- Response always 200 OK on success (even when empty).

Response shape:

```json
{
  "events": [
    {
      "cursor": "MDA...",
      "runId": "kcsnap_ai-sdlc-platform_123",
      "occurredAt": "2026-06-02T14:32:11.234Z",
      "eventType": "AgentCompleted",
      "data": { "agentName": "Architect", "decision": "ContinueAutonomously", "riskLevel": "Low", "summary": "..." }
    }
  ],
  "nextCursor": "MDA...",
  "hasMore": true
}
```

- Each event has its own `cursor` — clients can resume from any specific event.
- `nextCursor` is the cursor of the last event in the page (or the input cursor if the page is empty).
- `hasMore: true` means another page is immediately available; `false` means the caller has caught up to the head of the stream and should back off.
- Empty page → `events: []`, `nextCursor` = input cursor, `hasMore: false`.

Caller drives poll interval. The server does not implement long-polling, server-sent events, or rate-limit headers in v1.

### Event envelope

Every event in the stream is shaped as:

```json
{
  "cursor": "<opaque>",
  "runId": "<runId>",
  "occurredAt": "<ISO 8601 UTC>",
  "eventType": "<stable enum string>",
  "data": { /* type-specific payload */ }
}
```

`data` is polymorphic by `eventType`. C# representation in the contract package uses `System.Text.Json` polymorphism (`[JsonPolymorphic]` + `[JsonDerivedType]`) so consumers get compile-time-typed access to the discriminated payload.

### Event taxonomy (v1)

Ten stable event types, all mapped from the existing de facto audit taxonomy:

| `eventType` | Source `AuditEvent` shape | Meaning |
|---|---|---|
| `WebhookReceived` | `ActorType=Webhook`, `Action=issues.opened` / `issue_comment.created` / `pull_request.opened` etc. | GitHub webhook landed on the orchestrator. |
| `WorkflowStarted` | First emission per RunId. | Orchestrator instance created for the issue. |
| `AgentStarted` | `ActorType=Agent`, `Action=Started`. | Persona agent began executing. |
| `AgentCompleted` | `ActorType=Agent`, `Action=Completed`. | Persona agent finished successfully. |
| `AgentFailed` | `ActorType=Agent`, `Action=Failed`. | Persona agent raised an exception. |
| `CommentPosted` | `ActorType=Comment`, `Action=Posted`. | Orchestrator posted a markdown comment to the issue/PR. |
| `WorkflowReleased` | `ActorType=Workflow`, `Action=Released`. | Terminal success: PR merged, deployment recorded. |
| `WorkflowStopped` | `ActorType=Workflow`, `Action=Stopped`. | Terminal: human stop, gate failure, or workflow-level abort. |
| `WorkflowFailed` | `ActorType=Workflow`, `Action=Failed`. | Terminal: unrecoverable exception. |
| `BootstrapTerminalMarker` | New event emitted alongside the existing HTML-comment marker. | Bootstrap-mode-only completion signal. See "Terminal markers relationship" below. |

Adding a new `eventType` in a future version is a **minor** version bump (additive, non-breaking). Removing or repurposing one is a **major** version bump.

### Per-event `data` shape

Each `eventType` maps to a concrete C# record in `AiSdlc.Events.Contract`. Field names mirror the `AuditEvent` field names where reasonable so the mapping is mechanical. Common fields (`runId`, `occurredAt`) live on the envelope, not in `data`. Examples:

```csharp
public sealed record AgentCompletedData(
    string AgentName,
    string Summary,
    string? Decision,
    string? RiskLevel,
    string? CommitSha);

public sealed record CommentPostedData(
    string Summary,
    string CommentUrl,
    long CommentId);

public sealed record BootstrapTerminalMarkerData(
    string Status); // "completed" | "failed"
```

Full per-type schema is normative and lives in the contract package source — this ADR locks the envelope + taxonomy; per-type fields can evolve additively without an ADR amendment.

### Contract package (`AiSdlc.Events.Contract`)

A new project published as a NuGet package to **GitHub Packages** on the `kcsnap` org. Contains **only** DTOs, enums, and `JsonSerializerOptions` helpers — no behavior, no transport code, no I/O.

Versioning: **SemVer**.
- **Major** — breaking schema change (envelope field renamed/removed, event type removed, `data` field removed or retyped).
- **Minor** — additive (new event type, new optional `data` field).
- **Patch** — non-breaking serialization tweak, doc fix.

Yorrixx (and any future consumer) adds:

```xml
<PackageReference Include="AiSdlc.Events.Contract" Version="1.*" />
```

Compile-time-typed deserialization on both sides. Drift detection is automatic — schema changes that escape semver fail consumer builds.

### Terminal markers (PR #51) relationship

The HTML-comment terminal markers from PR #51 stay in place. The events API does **not** replace them in v1; it runs alongside them.

- Bootstrap-mode workflows continue to emit `<!-- ai-sdlc:status=completed -->` / `<!-- ai-sdlc:status=failed -->` as PR comments.
- The same orchestrator step also emits a `BootstrapTerminalMarker` event into the stream.
- Yorrixx **should migrate** to the events API as the primary completion signal; markers become the **fallback** for periods when the API is unreachable (network blip, key rotation, etc.).
- Deprecation and removal of HTML-comment markers is a **v2** concern, gated on Yorrixx confirming production use of the events API.

This belt-and-braces approach is intentional: the platform cannot afford to silently break Yorrixx's "Building → Live" state transition during the migration.

### What this ADR does NOT lock

- Specific `data`-record field lists per `eventType` (locked in code in the contract package, additively versioned).
- Concrete Function App configuration values (storage account, key vault references, etc.).
- CI/CD plumbing for publishing the NuGet package (handled in the implementation PR).
- Yorrixx-side consumer code (separate repo, separate session).
- Rate limiting, CORS, deletion / RTBF semantics — not relevant for an internal platform→Yorrixx API in v1.

## Consequences

### Improvements

- **Yorrixx gets a typed, paginated, monotonic event stream** instead of scraping invisible HTML comments. Latency drops from "next time we look at a PR comment" to "next poll interval" (~1–2 s).
- **Stable, versioned contract** decoupled from platform internals. Storage backing, hosting topology, and auth model can all evolve without breaking clients.
- **Compile-time safety on both sides** via the NuGet package. Schema drift fails CI, not production.
- **Migration path to push transport** (SSE / SignalR) preserved — same envelope shape, different delivery — without a contract break.

### Known limits (accepted v1 tradeoffs)

- **Poll-based.** Minimum latency = client poll interval. Yorrixx is expected to poll at 1–2 s while a run is active. Push transport is a v2 upgrade if responsiveness becomes a complaint.
- **Single shared API key.** Rotation is manual. Multi-tenant per-key scoping deferred to a future ADR when Yorrixx hosts multiple distinct orgs.
- **Shared hosting.** Reads compete with webhook receipts for orchestrator scale until the v2 split. Acceptable because read traffic is expected to be ≤ 10× webhook traffic and orchestrator capacity has substantial headroom.
- **RunId leaks GitHub identity.** `{owner}_{repo}_{issueNumber}` is human-readable. Acceptable for v1 where Yorrixx and the platform share the `kcsnap`/`yorrixx-apps` org scope.
- **No backpressure signaling.** Server returns `hasMore` only; no rate-limit headers, no 429 responses. Yorrixx is expected to back off via `hasMore: false` and tune its own poll interval.
- **Storage-bound query latency.** `GetSinceAsync` is a Table Storage query under the PartitionKey — fast, but cold-start on Flex Consumption (per ADR-0003) is ~1–2 s for the orchestrator host. Yorrixx should expect occasional cold-start latency spikes.
- **Bootstrap markers stay.** Until v2 removes them, the platform pays for two completion-signal mechanisms simultaneously.

### Downstream implementation work (out of scope for this ADR)

Three follow-up PRs, each with its own issue:

1. **`AiSdlc.Events.Contract` project + DTOs + GitHub Packages publish.** Adds the `.csproj`, the envelope record, the 10 event-type discriminator + per-type `data` records, the `JsonSerializerOptions` helper, and the `.github/workflows/publish-contract.yml` workflow that publishes on tag.
2. **`EventsApiFunction` in `AiSdlc.Orchestrator`.** HTTP-triggered Function at `/v1/runs/{runId}/events`, function-level key auth, reads via the existing `IAuditService`, maps `AuditEvent` to typed envelope, cursor encode/decode, pagination per spec.
3. **`BootstrapTerminalMarker` event emission.** Wire emission in `AiSdlcWorkflowOrchestrator.cs` alongside the existing `PostBootstrapStatusMarkerAsync` call sites (both successful-merge and `RecordWorkflowExitAsync` paths). Verify the HTML-comment marker continues to emit unchanged.

After the three platform-side PRs land, the Yorrixx-side migration is a separate piece of work owned by the yorrixx-app session.

## References

- Architecture decision #9 (Yorrixx app architecture memory) — original commitment to the events API.
- PR #51 / commit `d3fc42b` — Bootstrap terminal-status markers (the stop-gap this contract supersedes).
- `src/AiSdlc.Audit/IAuditService.cs`, `src/AiSdlc.Audit/AzureTableAuditService.cs` — current event-storage surface.
- `src/AiSdlc.Orchestrator/AiSdlcWorkflowOrchestrator.cs:568,645–651` — terminal-marker emission points.
- `src/AiSdlc.Dashboard/Services/AuditFeedService.cs` — the existing poll-based consumption pattern that the events API generalises.
