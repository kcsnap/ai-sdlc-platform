namespace AiSdlc.Events.Contract;

/// <summary>
/// Response shape for <c>GET /v1/runs/{runId}/events</c>.
/// Empty page → <see cref="Events"/> empty, <see cref="NextCursor"/> equals input cursor, <see cref="HasMore"/> false.
/// </summary>
/// <param name="Events">Events in cursor order (oldest first).</param>
/// <param name="NextCursor">Cursor of the last event in the page, or the input cursor when the page is empty. Pass back as <c>?since=</c> to continue.</param>
/// <param name="HasMore">True when another page is immediately available; false when the caller has caught up to the head of the stream.</param>
public sealed record EventsResponse(
    IReadOnlyList<EventEnvelope> Events,
    string NextCursor,
    bool HasMore);
