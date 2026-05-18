using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiSdlc.Agents;
using AiSdlc.Audit;
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
    private readonly IAuditService _audit;

    public GitHubWebhookFunction(ILogger<GitHubWebhookFunction> logger, IAuditService audit)
    {
        _logger = logger;
        _audit = audit;
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
        if (payload is null)
            return Accepted(request);

        var issueRefs = new Dictionary<string, string> { ["issueTitle"] = payload.Issue.Title };
        if (!string.IsNullOrWhiteSpace(payload.Issue.State))
            issueRefs["issueState"] = payload.Issue.State!;
        if (!string.IsNullOrWhiteSpace(payload.Issue.StateReason))
            issueRefs["issueStateReason"] = payload.Issue.StateReason!;

        await WriteWebhookAuditAsync(
            repository:  payload.Repository.FullName,
            issueNumber: payload.Issue.Number,
            prNumber:    null,
            actionLabel: $"issues.{payload.Action}",
            summary:     $"Issue #{payload.Issue.Number} {payload.Action}: {payload.Issue.Title}",
            cancellationToken: cancellationToken,
            references:  issueRefs);

        if (payload.Action is not ("opened" or "reopened"))
            return Accepted(request);

        var repository  = payload.Repository.FullName;
        var issueNumber = payload.Issue.Number;
        var instanceId  = BuildInstanceId(repository, issueNumber);

        var existing = await durableClient.GetInstanceAsync(instanceId, cancellation: cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsCompleted)
            {
                _logger.LogInformation("Orchestration {InstanceId} already active (status: {Status}). Ignoring.", instanceId, existing.RuntimeStatus);
                return Accepted(request);
            }
            _logger.LogInformation("Orchestration {InstanceId} in terminal state {Status} — purging for restart.", instanceId, existing.RuntimeStatus);
            await durableClient.PurgeInstanceAsync(instanceId, cancellation: cancellationToken);
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
        if (payload is null)
            return Accepted(request);

        await WriteWebhookAuditAsync(
            repository:  payload.Repository.FullName,
            issueNumber: payload.Issue.Number,
            prNumber:    null,
            actionLabel: $"issue_comment.{payload.Action}",
            summary:     $"Comment {payload.Action} on issue #{payload.Issue.Number} by {payload.Comment.User.Login}",
            cancellationToken: cancellationToken);

        if (payload.Action != "created")
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
                WorkflowCommand.ApproveMerge   => WorkflowEventNames.HumanReviewApproved,
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

        var prIssueGuess = ExtractIssueNumber(payload.PullRequest.Head.Ref, payload.PullRequest.Body) ?? 0;
        await WriteWebhookAuditAsync(
            repository:  payload.Repository.FullName,
            issueNumber: prIssueGuess,
            prNumber:    payload.PullRequest.Number,
            actionLabel: $"pull_request.{payload.Action}",
            summary:     $"PR #{payload.PullRequest.Number} {payload.Action} on branch {payload.PullRequest.Head.Ref}",
            cancellationToken: cancellationToken);

        if (payload.Action is not ("opened" or "synchronize"))
            return Accepted(request);

        var repository = payload.Repository.FullName;
        var prNumber   = payload.PullRequest.Number;
        var headBranch = payload.PullRequest.Head.Ref;
        var headSha    = payload.PullRequest.Head.Sha;

        _logger.LogInformation("PR {PrNumber} ({Action}) on {Repository} branch {Branch}.", prNumber, payload.Action, repository, headBranch);

        var issueNumber = ExtractIssueNumber(headBranch, payload.PullRequest.Body);
        if (issueNumber is null)
        {
            _logger.LogWarning("Could not extract issue number from PR {PrNumber} branch '{Branch}' — skipping.", prNumber, headBranch);
            return Accepted(request);
        }

        var instanceId = BuildInstanceId(repository, issueNumber.Value);
        var prPayload  = new PrReadyPayload(prNumber, headSha);

        try
        {
            await durableClient.RaiseEventAsync(instanceId, WorkflowEventNames.PullRequestReady, prPayload, cancellation: cancellationToken);
            _logger.LogInformation("Raised PullRequestReady for orchestration {InstanceId} (PR #{PrNumber}).", instanceId, prNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not raise PullRequestReady on {InstanceId}.", instanceId);
        }

        return Accepted(request);
    }

    private static int? ExtractIssueNumber(string branchName, string? prBody)
    {
        // Primary: branch name convention ai/{issueNumber}-... or ai/{issueNumber}_...
        var branchMatch = Regex.Match(branchName, @"[a-z]+/(\d+)[-_]", RegexOptions.IgnoreCase);
        if (branchMatch.Success && int.TryParse(branchMatch.Groups[1].Value, out var fromBranch))
            return fromBranch;

        // Fallback: "Closes #N" / "Fixes #N" in PR body
        if (!string.IsNullOrWhiteSpace(prBody))
        {
            var bodyMatch = Regex.Match(prBody, @"(?:closes?|fixes?)\s+#(\d+)", RegexOptions.IgnoreCase);
            if (bodyMatch.Success && int.TryParse(bodyMatch.Groups[1].Value, out var fromBody))
                return fromBody;
        }

        return null;
    }

    private async Task WriteWebhookAuditAsync(
        string repository, int issueNumber, int? prNumber, string actionLabel, string summary, CancellationToken cancellationToken,
        Dictionary<string, string>? references = null)
    {
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId             = BuildInstanceId(repository, issueNumber),
                Repository        = repository,
                IssueNumber       = issueNumber,
                PullRequestNumber = prNumber,
                ActorType         = "Webhook",
                ActorName         = "/github/webhook",
                Action            = actionLabel,
                Summary           = summary,
                References        = references ?? new Dictionary<string, string>()
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit writes must never break webhook processing.
            _logger.LogWarning(ex, "Failed to write webhook audit event for {Action}.", actionLabel);
        }
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
