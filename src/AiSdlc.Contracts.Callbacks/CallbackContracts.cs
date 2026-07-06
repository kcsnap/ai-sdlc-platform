namespace AiSdlc.Contracts.Callbacks;

// Platform-owned build-callback contract (A11, direction locked 2026-07-06). These are the payloads the
// platform POSTs to {callbackBaseUrl}/apps/{appId}/{kind} on yorrixx-app's admin API, promoted from the
// anonymous objects in NewAppBuildOrchestrator/CostEmittingModelProvider.
//
// WIRE SHAPE (unchanged, golden-pinned in AiSdlc.Orchestrator.Tests/CallbackWireShapeTests): the SENDER
// serializes with camelCase naming + WhenWritingNull — these records carry no serializer attributes, so
// property DECLARATION ORDER is the wire order. Do not reorder members.

/// <summary>
/// The canonical build-status vocabulary (9 values, ratified responsibility-split-phase0-contracts §status):
/// the 7 pipeline states the platform emits on /status plus the delete-flow pair (deleting → archived)
/// yorrixx maps into its AppStatuses. String constants, not an enum: the wire carries kebab-case strings.
/// </summary>
public static class CanonicalBuildStatus
{
    /// <summary>Build accepted; nothing has run yet.</summary>
    public const string Queued = "queued";
    /// <summary>Cloud resources are being provisioned (Call 1 → provisioner).</summary>
    public const string Provisioning = "provisioning";
    /// <summary>App content/deploy workflow is being produced and pushed.</summary>
    public const string Building = "building";
    /// <summary>Verification gate: deploy workflow polled + hosted URL probed.</summary>
    public const string Verifying = "verifying";
    /// <summary>Verification passed; awaiting the (auto-approving in dev) review gate.</summary>
    public const string ReadyForReview = "ready-for-review";
    /// <summary>App is live at its hosted URL (fires the publish email).</summary>
    public const string Live = "live";
    /// <summary>Terminal failure; Detail carries the reason (yorrixx maps to *-failed + lastError).</summary>
    public const string Failed = "failed";
    /// <summary>Delete requested; teardown in progress (yorrixx-side transition).</summary>
    public const string Deleting = "deleting";
    /// <summary>Teardown complete; app retired (yorrixx-side terminal state).</summary>
    public const string Archived = "archived";

    /// <summary>Every canonical value, pipeline order then the delete pair.</summary>
    public static readonly IReadOnlyList<string> All =
        [Queued, Provisioning, Building, Verifying, ReadyForReview, Live, Failed, Deleting, Archived];
}

/// <summary>POST …/apps/{appId}/status — pipeline progress.</summary>
/// <param name="Status">A <see cref="CanonicalBuildStatus"/> value.</param>
/// <param name="Phase">Pipeline phase label (e.g. "Provision", "Verify"); omitted when null.</param>
/// <param name="Detail">Failure/context detail; omitted when null.</param>
public sealed record StatusCallback(string Status, string? Phase = null, string? Detail = null);

/// <summary>
/// POST …/apps/{appId}/runtime — where the app lives, sent before any 'live' status so the publish email
/// carries the hosted URL. RepoUrl has been part of this payload since the emit was introduced (F2 audit).
/// </summary>
/// <param name="RepoUrl">HTML URL of the generated user-app repository.</param>
/// <param name="HostedUrl">Public URL of the deployed app; omitted when null (pre-deploy failure paths).</param>
public sealed record RuntimeCallback(string RepoUrl, string? HostedUrl);

/// <summary>One row of the verification check table.</summary>
/// <param name="CheckId">Stable check identifier (e.g. "deploy-run-green", "frontend-serves-app").</param>
/// <param name="Name">Human-readable check name.</param>
/// <param name="Status">"pass" | "fail" | "skipped".</param>
/// <param name="Evidence">Short evidence string (≤500 chars); omitted when null.</param>
/// <param name="At">ISO-8601 timestamp of the verification pass.</param>
public sealed record VerificationCheck(string CheckId, string Name, string Status, string? Evidence, string At);

/// <summary>POST …/apps/{appId}/verification — the verification gate outcome + check table.</summary>
/// <param name="Outcome">"passed" | "failed".</param>
/// <param name="Attempt">1-based verification attempt.</param>
/// <param name="Checks">The check table rows.</param>
public sealed record VerificationCallback(string Outcome, int Attempt, IReadOnlyList<VerificationCheck> Checks);

/// <summary>
/// POST …/apps/{appId}/cost — per-LLM-call cost telemetry (raw Anthropic usage; £ derived downstream).
/// Relocated verbatim from AiSdlc.Orchestrator.Cost (the sender keeps its idempotency-key headers).
/// </summary>
public sealed record BuildCostCallback
{
    /// <summary>Anthropic model id that served the call.</summary>
    public string Model { get; init; } = string.Empty;
    /// <summary>Cost-attribution phase bucket (brief / review / code-gen / fix-loop / test-impl / other).</summary>
    public string Phase { get; init; } = string.Empty;
    /// <summary>Build iteration the call belongs to.</summary>
    public int Iteration { get; init; }
    /// <summary>Raw Anthropic input tokens.</summary>
    public long InputTokens { get; init; }
    /// <summary>Raw Anthropic output tokens.</summary>
    public long OutputTokens { get; init; }
    /// <summary>Prompt-cache read tokens.</summary>
    public long CacheReadTokens { get; init; }
    /// <summary>Prompt-cache write tokens.</summary>
    public long CacheWriteTokens { get; init; }
    /// <summary>Number of API calls aggregated into this row (normally 1).</summary>
    public int Calls { get; init; } = 1;
    /// <summary>Anthropic request id for traceability; omitted when null.</summary>
    public string? RequestId { get; init; }
}
