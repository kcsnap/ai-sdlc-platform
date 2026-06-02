using System.Text.Json.Serialization;

namespace AiSdlc.Events.Contract;

/// <summary>
/// One event from a platform run, as exposed by <c>GET /v1/runs/{runId}/events</c>.
/// The <see cref="EventType"/> discriminator selects which concrete <see cref="EventData"/> subtype carries the <see cref="Data"/> payload.
/// JSON shape locked by ADR-0004; envelope-level fields are common to every event.
/// </summary>
[JsonConverter(typeof(EventEnvelopeJsonConverter))]
public sealed record EventEnvelope
{
    /// <summary>Opaque, monotonic cursor for resuming the stream. Treat as a string token; do not parse client-side.</summary>
    public required string Cursor { get; init; }

    /// <summary>Run identity: <c>{owner}_{repo}_{issueNumber}</c>.</summary>
    public required string RunId { get; init; }

    /// <summary>UTC timestamp at which the underlying activity occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Discriminator for <see cref="Data"/>. Unknown values deserialize to <see cref="UnknownEventData"/> with <see cref="Contract.EventType.Unknown"/>.</summary>
    public required EventType EventType { get; init; }

    /// <summary>GitHub repository in <c>owner/repo</c> form.</summary>
    public required string Repository { get; init; }

    /// <summary>Issue number this run is keyed off.</summary>
    public required int IssueNumber { get; init; }

    /// <summary>Pull request number if the event arose from a PR-scoped activity.</summary>
    public int? PullRequestNumber { get; init; }

    /// <summary>True if any platform-side redaction was applied to the payload before storage.</summary>
    public bool RedactionApplied { get; init; }

    /// <summary>Type-discriminated payload. Concrete subtype determined by <see cref="EventType"/>.</summary>
    public required EventData Data { get; init; }
}
