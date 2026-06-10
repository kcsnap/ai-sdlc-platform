namespace AiSdlc.Orchestrator.Webhooks;

/// <summary>
/// Durable intake for validated webhook deliveries. The HTTP receiver persists the raw
/// event here and ACKs GitHub immediately; a queue-triggered function processes it with
/// host-level retries and a poison queue, so downstream failures never surface to GitHub
/// as a 5xx (GitHub does not retry failed deliveries).
/// </summary>
public interface IWebhookInbox
{
    Task EnqueueAsync(string eventType, string? deliveryId, byte[] body, CancellationToken cancellationToken);

    /// <summary>Returns the raw payload bytes for an envelope, fetching from blob when offloaded.</summary>
    Task<byte[]> ResolveBodyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken);
}
