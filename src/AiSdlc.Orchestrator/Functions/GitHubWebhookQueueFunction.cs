using System.Text.Json;
using AiSdlc.Orchestrator.Webhooks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Async half of the fast-ACK webhook intake: drains the inbox queue and runs the real
/// event processing. A throwing invocation is retried by the Functions host (5 attempts)
/// and then parked on the poison queue — the delivery is never lost.
/// </summary>
public sealed class GitHubWebhookQueueFunction
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<GitHubWebhookQueueFunction> _logger;
    private readonly IWebhookInbox _inbox;
    private readonly GitHubWebhookProcessor _processor;

    public GitHubWebhookQueueFunction(
        ILogger<GitHubWebhookQueueFunction> logger, IWebhookInbox inbox, GitHubWebhookProcessor processor)
    {
        _logger    = logger;
        _inbox     = inbox;
        _processor = processor;
    }

    [Function(nameof(GitHubWebhookQueueFunction))]
    public async Task RunAsync(
        [QueueTrigger(StorageQueueWebhookInbox.QueueName, Connection = "AzureWebJobsStorage")] string message,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<WebhookEnvelope>(message, JsonOpts)
            ?? throw new InvalidOperationException("Webhook inbox message did not deserialize to a WebhookEnvelope.");

        _logger.LogInformation("Processing queued webhook {EventType} (delivery {DeliveryId}).",
            envelope.EventType, envelope.DeliveryId);

        var body = await _inbox.ResolveBodyAsync(envelope, cancellationToken);
        await _processor.ProcessAsync(envelope.EventType, body, durableClient, cancellationToken);
    }
}
