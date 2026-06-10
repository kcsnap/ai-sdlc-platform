using System.Net;
using AiSdlc.GitHub;
using AiSdlc.GitHub.Webhooks;
using AiSdlc.Orchestrator.Webhooks;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Fast-ACK webhook receiver. Validates the HMAC signature, persists the raw event to the
/// webhook inbox queue and returns 202 immediately — GitHub never retries failed deliveries,
/// so downstream latency or failures must not surface here as a 5xx (v17 incident: one
/// cold-start 502 on issues.opened stranded a build at ai-sdlc:bootstrap permanently).
/// Processing happens in <see cref="GitHubWebhookQueueFunction"/> with host retries and a
/// poison queue; if the queue itself is unavailable the event is processed inline instead.
/// </summary>
public sealed class GitHubWebhookFunction
{
    /// <inheritdoc cref="GitHubWebhookProcessor.BootstrapLabel"/>
    public const string BootstrapLabel = GitHubWebhookProcessor.BootstrapLabel;

    private readonly ILogger<GitHubWebhookFunction> _logger;
    private readonly IWebhookInbox _inbox;
    private readonly GitHubWebhookProcessor _processor;

    public GitHubWebhookFunction(
        ILogger<GitHubWebhookFunction> logger, IWebhookInbox inbox, GitHubWebhookProcessor processor)
    {
        _logger    = logger;
        _inbox     = inbox;
        _processor = processor;
    }

    [Function(nameof(GitHubWebhookFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "github/webhook")] HttpRequestData request,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
    {
        // Read body bytes first — needed for signature validation before deserialization
        var bodyBytes = await ReadBodyAsync(request, cancellationToken);

        if (!ValidateSignature(request, bodyBytes))
        {
            _logger.LogWarning("GitHub webhook signature validation failed.");
            return request.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var eventType  = HeaderOrEmpty(request, "X-GitHub-Event");
        var deliveryId = HeaderOrEmpty(request, "X-GitHub-Delivery");

        _logger.LogInformation("GitHub webhook received: {EventType} (delivery {DeliveryId})", eventType, deliveryId);

        try
        {
            await _inbox.EnqueueAsync(eventType, deliveryId, bodyBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            // Queue unavailable — process inline rather than dropping the event.
            _logger.LogWarning(ex, "Webhook inbox enqueue failed for delivery {DeliveryId} — processing inline.", deliveryId);
            await _processor.ProcessAsync(eventType, bodyBytes, durableClient, cancellationToken);
        }

        return request.CreateResponse(HttpStatusCode.Accepted);
    }

    public static WorkflowMode ResolveWorkflowMode(IEnumerable<WebhookLabel> labels) =>
        GitHubWebhookProcessor.ResolveWorkflowMode(labels);

    private static string HeaderOrEmpty(HttpRequestData request, string name) =>
        request.Headers.TryGetValues(name, out var vals)
            ? vals.FirstOrDefault() ?? string.Empty
            : string.Empty;

    private bool ValidateSignature(HttpRequestData request, byte[] bodyBytes)
    {
        var secret = Environment.GetEnvironmentVariable("GitHubWebhookSecret");
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("GitHubWebhookSecret is not configured — skipping signature validation.");
            return true;
        }

        if (!request.Headers.TryGetValues("X-Hub-Signature-256", out var sigValues))
        {
            _logger.LogWarning("Missing X-Hub-Signature-256 header.");
            return false;
        }

        var signature = sigValues.FirstOrDefault() ?? string.Empty;
        return GitHubWebhookValidator.IsValid(bodyBytes, signature, secret);
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }
}
