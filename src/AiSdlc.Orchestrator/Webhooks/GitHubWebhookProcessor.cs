using System.Text.Json;
using System.Text.RegularExpressions;
using AiSdlc.Agents;
using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.GitHub.Webhooks;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Webhooks;

/// <summary>
/// Handles validated GitHub webhook payloads: audits the delivery, starts orchestrations
/// for issue opened/reopened, and raises external events for commands and PR activity.
/// Shared by the HTTP receiver (inline fallback) and the queue-triggered processor.
/// </summary>
public sealed class GitHubWebhookProcessor
{
    // Issue label that opts a run into Bootstrap mode (greenfield, fully unattended).
    // Set by Yorrixx when filing the initial "build this app" issue on a user-app repo.
    public const string BootstrapLabel = "ai-sdlc:bootstrap";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<GitHubWebhookProcessor> _logger;
    private readonly IAuditService _audit;

    public GitHubWebhookProcessor(ILogger<GitHubWebhookProcessor> logger, IAuditService audit)
    {
        _logger = logger;
        _audit  = audit;
    }

    public Task ProcessAsync(string eventType, byte[] body, DurableTaskClient durableClient, CancellationToken cancellationToken) =>
        eventType switch
        {
            "issues"        => HandleIssueEventAsync(body, durableClient, cancellationToken),
            "issue_comment" => HandleIssueCommentEventAsync(body, durableClient, cancellationToken),
            "pull_request"  => HandlePullRequestEventAsync(body, durableClient, cancellationToken),
            _               => Task.CompletedTask  // Unknown events acknowledged but not processed
        };

    private async Task HandleIssueEventAsync(byte[] body, DurableTaskClient durableClient, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<IssueWebhookPayload>(body, JsonOptions);
        if (payload is null)
            return;

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
            return;

        var repository  = payload.Repository.FullName;
        var issueNumber = payload.Issue.Number;
        var instanceId  = BuildInstanceId(repository, issueNumber);

        var existing = await durableClient.GetInstanceAsync(instanceId, cancellation: cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsCompleted)
            {
                _logger.LogInformation("Orchestration {InstanceId} already active (status: {Status}). Ignoring.", instanceId, existing.RuntimeStatus);
                return;
            }
            _logger.LogInformation("Orchestration {InstanceId} in terminal state {Status} — purging for restart.", instanceId, existing.RuntimeStatus);
            await durableClient.PurgeInstanceAsync(instanceId, cancellation: cancellationToken);
        }

        var agentContext = BuildAgentContext(
            instanceId, repository, issueNumber,
            ResolveWorkflowMode(payload.Issue.Labels),
            payload.Issue.Title, payload.Issue.Body, payload.Issue.Url, payload.Issue.User.Login);

        // A reopen means the previous run's output failed downstream verification — the
        // orchestrator fetches the findings comments and feeds them to every agent (#88).
        if (payload.Action == "reopened")
            agentContext.Metadata["reopened"] = "true";

        await durableClient.ScheduleNewOrchestrationInstanceAsync(
            nameof(AiSdlcWorkflowOrchestrator),
            agentContext,
            new StartOrchestrationOptions { InstanceId = instanceId },
            cancellationToken);  // positional — CT param is named 'cancellation' in the SDK

        _logger.LogInformation("Started orchestration {InstanceId} for {Repository}#{IssueNumber}.", instanceId, repository, issueNumber);
    }

    private async Task HandleIssueCommentEventAsync(byte[] body, DurableTaskClient durableClient, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<IssueCommentWebhookPayload>(body, JsonOptions);
        if (payload is null)
            return;

        await WriteWebhookAuditAsync(
            repository:  payload.Repository.FullName,
            issueNumber: payload.Issue.Number,
            prNumber:    null,
            actionLabel: $"issue_comment.{payload.Action}",
            summary:     $"Comment {payload.Action} on issue #{payload.Issue.Number} by {payload.Comment.User.Login}",
            cancellationToken: cancellationToken);

        if (payload.Action != "created")
            return;

        var command = WorkflowCommandParser.Parse(payload.Comment.Body);
        if (command == WorkflowCommand.None)
            return;

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
                WorkflowCommand.Retry          => WorkflowEventNames.RetryStage,
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
    }

    private async Task HandlePullRequestEventAsync(byte[] body, DurableTaskClient durableClient, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PullRequestWebhookPayload>(body, JsonOptions);
        if (payload is null)
            return;

        var prIssueGuess = ExtractIssueNumber(payload.PullRequest.Head.Ref, payload.PullRequest.Body) ?? 0;
        await WriteWebhookAuditAsync(
            repository:  payload.Repository.FullName,
            issueNumber: prIssueGuess,
            prNumber:    payload.PullRequest.Number,
            actionLabel: $"pull_request.{payload.Action}",
            summary:     $"PR #{payload.PullRequest.Number} {payload.Action} on branch {payload.PullRequest.Head.Ref}",
            cancellationToken: cancellationToken);

        if (payload.Action is not ("opened" or "synchronize"))
            return;

        var repository = payload.Repository.FullName;
        var prNumber   = payload.PullRequest.Number;
        var headBranch = payload.PullRequest.Head.Ref;
        var headSha    = payload.PullRequest.Head.Sha;

        _logger.LogInformation("PR {PrNumber} ({Action}) on {Repository} branch {Branch}.", prNumber, payload.Action, repository, headBranch);

        var issueNumber = ExtractIssueNumber(headBranch, payload.PullRequest.Body);
        if (issueNumber is null)
        {
            _logger.LogWarning("Could not extract issue number from PR {PrNumber} branch '{Branch}' — skipping.", prNumber, headBranch);
            return;
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
    }

    /// <summary>
    /// Builds the orchestration input shared by the webhook path and the reconciliation sweep.
    /// </summary>
    public static AgentContext BuildAgentContext(
        string instanceId, string repository, int issueNumber, WorkflowMode mode,
        string issueTitle, string? issueBody, string issueUrl, string issueAuthor) =>
        new()
        {
            RunId          = instanceId,
            Repository     = repository,
            IssueNumber    = issueNumber,
            CurrentState   = WorkflowRunStatus.Started.ToString(),
            RequestedAgent = AgentNames.ProductStrategist,
            Mode           = mode,
            Metadata       =
            {
                ["issueTitle"]   = issueTitle,
                ["issueBody"]    = issueBody ?? string.Empty,
                ["issueUrl"]     = issueUrl,
                ["issueAuthor"]  = issueAuthor
            }
        };

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

    public static WorkflowMode ResolveWorkflowMode(IEnumerable<WebhookLabel> labels) =>
        labels.Any(l => string.Equals(l.Name, BootstrapLabel, StringComparison.OrdinalIgnoreCase))
            ? WorkflowMode.Bootstrap
            : WorkflowMode.Standard;

    public static string BuildInstanceId(string repository, int issueNumber)
    {
        // Slashes are replaced to produce a safe Durable Functions instance ID.
        // Format: owner_repo_123 — stable across retried webhook deliveries.
        var safeRepo = repository.Replace('/', '_');
        return $"{safeRepo}_{issueNumber}";
    }
}
