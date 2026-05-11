using System.Net;
using System.Text.Json;
using AiSdlc.Agents;
using AiSdlc.GitHub;
using AiSdlc.GitHub.Webhooks;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

public sealed class GitHubWebhookFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<GitHubWebhookFunction> _logger;

    public GitHubWebhookFunction(ILogger<GitHubWebhookFunction> logger)
    {
        _logger = logger;
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

        var eventType = request.Headers.TryGetValues("X-GitHub-Event", out var vals)
            ? vals.FirstOrDefault() ?? string.Empty
            : string.Empty;

        _logger.LogInformation("GitHub webhook received: {EventType}", eventType);

        return eventType switch
        {
            "issues"        => await HandleIssueEventAsync(bodyBytes, durableClient, cancellationToken, request),
            "issue_comment" => await HandleIssueCommentEventAsync(bodyBytes, durableClient, cancellationToken, request),
            "pull_request"  => await HandlePullRequestEventAsync(bodyBytes, durableClient, cancellationToken, request),
            _               => Accepted(request)  // Unknown events acknowledged but not processed
        };
    }

    private async Task<HttpResponseData> HandleIssueEventAsync(
        byte[] body, DurableTaskClient durableClient, CancellationToken cancellationToken, HttpRequestData request)
    {
        var payload = JsonSerializer.Deserialize<IssueWebhookPayload>(body, JsonOptions);
        if (payload is null || payload.Action != "opened")
            return Accepted(request);

        var repository  = payload.Repository.FullName;
        var issueNumber = payload.Issue.Number;
        var instanceId  = BuildInstanceId(repository, issueNumber);

        var existing = await durableClient.GetInstanceAsync(instanceId, cancellation: cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Orchestration {InstanceId} already exists (status: {Status}). Ignoring duplicate.", instanceId, existing.RuntimeStatus);
            return Accepted(request);
        }

        var agentContext = new AgentContext
        {
            RunId          = instanceId,
            Repository     = repository,
            IssueNumber    = issueNumber,
            CurrentState   = WorkflowRunStatus.Started.ToString(),
            RequestedAgent = AgentNames.ProductStrategist,
            Metadata       =
            {
                ["issueTitle"]   = payload.Issue.Title,
                ["issueBody"]    = payload.Issue.Body ?? string.Empty,
                ["issueUrl"]     = payload.Issue.Url,
                ["issueAuthor"]  = payload.Issue.User.Login
            }
        };

        await durableClient.ScheduleNewOrchestrationInstanceAsync(
            nameof(AiSdlcWorkflowOrchestrator),
            agentContext,
            new StartOrchestrationOptions { InstanceId = instanceId },
            cancellationToken);  // positional — CT param is named 'cancellation' in the SDK

        _logger.LogInformation("Started orchestration {InstanceId} for {Repository}#{IssueNumber}.", instanceId, repository, issueNumber);
        return Accepted(request);
    }

    private async Task<HttpResponseData> HandleIssueCommentEventAsync(
        byte[] body, DurableTaskClient durableClient, CancellationToken cancellationToken, HttpRequestData request)
    {
        var payload = JsonSerializer.Deserialize<IssueCommentWebhookPayload>(body, JsonOptions);
        if (payload is null || payload.Action != "created")
            return Accepted(request);

        var command = WorkflowCommandParser.Parse(payload.Comment.Body);
        if (command == WorkflowCommand.None)
            return Accepted(request);

        var instanceId = BuildInstanceId(payload.Repository.FullName, payload.Issue.Number);
        _logger.LogInformation("Command {Command} detected for orchestration {InstanceId}.", command, instanceId);

        try
        {
            // Map WorkflowCommand → WorkflowEventNames so orchestrator and webhook use the same constants
            var eventName = command switch
            {
                WorkflowCommand.ApproveBrief   => WorkflowEventNames.ApproveBrief,
                WorkflowCommand.RequestChanges => WorkflowEventNames.RequestChanges,
                WorkflowCommand.ApproveRelease => WorkflowEventNames.ApproveRelease,
                _                              => command.ToString()
            };
            await durableClient.RaiseEventAsync(instanceId, eventName, cancellation: cancellationToken);
            _logger.LogInformation("Raised event {Command} on orchestration {InstanceId}.", command, instanceId);
        }
        catch (Exception ex)
        {
            // Orchestration may not yet be waiting for this event — log and carry on
            _logger.LogWarning(ex, "Could not raise event {Command} on {InstanceId}.", command, instanceId);
        }

        return Accepted(request);
    }

    private async Task<HttpResponseData> HandlePullRequestEventAsync(
        byte[] body, DurableTaskClient durableClient, CancellationToken cancellationToken, HttpRequestData request)
    {
        var payload = JsonSerializer.Deserialize<PullRequestWebhookPayload>(body, JsonOptions);
        if (payload is null)
            return Accepted(request);

        if (payload.Action is not ("opened" or "synchronize"))
            return Accepted(request);

        var repository  = payload.Repository.FullName;
        var prNumber    = payload.PullRequest.Number;
        var headBranch  = payload.PullRequest.Head.Ref;

        // Infer issue number from branch naming convention: ai/{issueNumber}-...
        // This can be extended to check PR body for "Closes #N" links
        _logger.LogInformation("PR {PrNumber} ({Action}) on {Repository} branch {Branch}.", prNumber, payload.Action, repository, headBranch);

        // Signal any running orchestration that a PR exists for this issue
        // The orchestrator will handle this event in a future iteration (section 7.3)
        _ = (durableClient, cancellationToken); // suppress warnings until 7.3 is wired up

        return Accepted(request);
    }

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

    private static string BuildInstanceId(string repository, int issueNumber)
    {
        // Slashes are replaced to produce a safe Durable Functions instance ID.
        // Format: owner_repo_123 — stable across retried webhook deliveries.
        var safeRepo = repository.Replace('/', '_');
        return $"{safeRepo}_{issueNumber}";
    }

    private static HttpResponseData Accepted(HttpRequestData request) =>
        request.CreateResponse(HttpStatusCode.Accepted);
}
