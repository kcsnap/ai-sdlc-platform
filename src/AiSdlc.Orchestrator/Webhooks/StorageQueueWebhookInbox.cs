using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace AiSdlc.Orchestrator.Webhooks;

public sealed class StorageQueueWebhookInbox : IWebhookInbox
{
    public const string QueueName             = "github-webhook-inbox";
    public const string OverflowContainerName = "webhook-inbox-overflow";

    // Azure queue messages cap at 64 KiB. Leave generous headroom for the JSON envelope
    // and the Base64 message encoding the Functions queue trigger expects.
    internal const int MaxInlineBodyBytes = 40 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly QueueClient _queue;
    private readonly BlobContainerClient _overflow;
    private volatile bool _infrastructureEnsured;

    public StorageQueueWebhookInbox(QueueClient queue, BlobContainerClient overflow)
    {
        _queue    = queue;
        _overflow = overflow;
    }

    public async Task EnqueueAsync(string eventType, string? deliveryId, byte[] body, CancellationToken cancellationToken)
    {
        await EnsureInfrastructureAsync(cancellationToken);

        string? inlineBody = null;
        string? blobName   = null;

        if (body.Length <= MaxInlineBodyBytes)
        {
            inlineBody = Encoding.UTF8.GetString(body);
        }
        else
        {
            blobName = $"{Guid.NewGuid():N}.json";
            await _overflow.UploadBlobAsync(blobName, new BinaryData(body), cancellationToken);
        }

        var envelope = new WebhookEnvelope(eventType, deliveryId, inlineBody, blobName);
        await _queue.SendMessageAsync(JsonSerializer.Serialize(envelope, JsonOpts), cancellationToken);
    }

    public async Task<byte[]> ResolveBodyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Body is not null)
            return Encoding.UTF8.GetBytes(envelope.Body);

        if (envelope.BodyBlobName is null)
            throw new InvalidOperationException("Webhook envelope has neither an inline body nor a blob reference.");

        var download = await _overflow.GetBlobClient(envelope.BodyBlobName).DownloadContentAsync(cancellationToken);
        return download.Value.Content.ToArray();
    }

    private async Task EnsureInfrastructureAsync(CancellationToken cancellationToken)
    {
        if (_infrastructureEnsured)
            return;

        await _queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await _overflow.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        _infrastructureEnsured = true;
    }
}
