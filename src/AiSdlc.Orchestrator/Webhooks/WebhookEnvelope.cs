namespace AiSdlc.Orchestrator.Webhooks;

/// <summary>
/// Queue message wrapping a signature-validated GitHub webhook delivery. Small payloads
/// travel inline in <see cref="Body"/>; payloads near the 64 KiB queue-message limit are
/// offloaded to blob storage and referenced via <see cref="BodyBlobName"/>.
/// Exactly one of <see cref="Body"/> / <see cref="BodyBlobName"/> is non-null.
/// </summary>
public sealed record WebhookEnvelope(
    string EventType,
    string? DeliveryId,
    string? Body,
    string? BodyBlobName);
