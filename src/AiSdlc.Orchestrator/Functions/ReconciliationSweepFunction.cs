using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.GitHub.Webhooks;
using AiSdlc.Orchestrator.Webhooks;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Self-healing net under the webhook intake: periodically scans the configured GitHub
/// organisation for open issues still labeled ai-sdlc:bootstrap that have no orchestration
/// record, and starts them. Catches any delivery lost end-to-end (missing repo webhook,
/// dropped 5xx delivery, queue poison) — a stranded build waits one sweep instead of forever.
/// Disabled unless the ReconciliationOrg app setting is present.
/// </summary>
public sealed class ReconciliationSweepFunction
{
    // Fresh issues are skipped — their webhook delivery may legitimately still be in flight.
    internal static readonly TimeSpan FreshIssueGrace = TimeSpan.FromMinutes(10);

    private readonly ILogger<ReconciliationSweepFunction> _logger;
    private readonly IGitHubService _gitHub;
    private readonly IAuditService _audit;

    public ReconciliationSweepFunction(
        ILogger<ReconciliationSweepFunction> logger, IGitHubService gitHub, IAuditService audit)
    {
        _logger = logger;
        _gitHub = gitHub;
        _audit  = audit;
    }

    [Function(nameof(ReconciliationSweepFunction))]
    public async Task RunAsync(
        [TimerTrigger("0 */15 * * * *")] TimerInfo timer,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
    {
        var organisation = Environment.GetEnvironmentVariable("ReconciliationOrg");
        if (string.IsNullOrWhiteSpace(organisation))
        {
            _logger.LogDebug("ReconciliationOrg is not configured — sweep skipped.");
            return;
        }

        var hits = await _gitHub.SearchOpenOrgIssuesByLabelAsync(
            organisation, GitHubWebhookProcessor.BootstrapLabel, cancellationToken);

        var started = 0;
        foreach (var hit in hits)
        {
            if (!ShouldReconcile(hit.Labels, hit.UpdatedAt, DateTimeOffset.UtcNow))
                continue;

            var instanceId = GitHubWebhookProcessor.BuildInstanceId(hit.Repository, hit.Number);
            var existing   = await durableClient.GetInstanceAsync(instanceId, cancellation: cancellationToken);
            if (existing is not null)
                continue;  // a run exists (any state) — the webhook path owns it

            var mode = GitHubWebhookProcessor.ResolveWorkflowMode(
                hit.Labels.Select(l => new WebhookLabel { Name = l }));

            var agentContext = GitHubWebhookProcessor.BuildAgentContext(
                instanceId, hit.Repository, hit.Number, mode,
                hit.Title, hit.Body, hit.Url, hit.Author);

            await durableClient.ScheduleNewOrchestrationInstanceAsync(
                nameof(AiSdlcWorkflowOrchestrator),
                agentContext,
                new StartOrchestrationOptions { InstanceId = instanceId },
                cancellationToken);

            await WriteSweepAuditAsync(hit, instanceId, cancellationToken);
            _logger.LogWarning(
                "Reconciliation started orchestration {InstanceId} for stranded bootstrap issue {Repository}#{Number} — " +
                "its issues.opened delivery never arrived.", instanceId, hit.Repository, hit.Number);
            started++;
        }

        if (started > 0)
            _logger.LogInformation("Reconciliation sweep started {Count} stranded run(s) in {Org}.", started, organisation);
    }

    /// <summary>
    /// A stranded issue qualifies when it is old enough that its webhook delivery cannot
    /// still be in flight, and carries no ai-sdlc progression label beyond bootstrap —
    /// progression labels prove a run already handled it (covers completed runs whose
    /// instances were purged by a later close/reopen).
    /// </summary>
    public static bool ShouldReconcile(IReadOnlyList<string> labels, DateTimeOffset updatedAt, DateTimeOffset nowUtc)
    {
        if (nowUtc - updatedAt < FreshIssueGrace)
            return false;

        return !labels.Any(l =>
            l.StartsWith("ai-sdlc:", StringComparison.OrdinalIgnoreCase)
            && !l.Equals(GitHubWebhookProcessor.BootstrapLabel, StringComparison.OrdinalIgnoreCase));
    }

    private async Task WriteSweepAuditAsync(OrgIssueSearchHit hit, string instanceId, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = instanceId,
                Repository  = hit.Repository,
                IssueNumber = hit.Number,
                ActorType   = "Reconciliation",
                ActorName   = "ReconciliationSweep",
                Action      = "reconciliation.started",
                Summary     = $"Started stranded bootstrap issue #{hit.Number}: {hit.Title}"
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit writes must never break the sweep.
            _logger.LogWarning(ex, "Failed to write reconciliation audit event for {InstanceId}.", instanceId);
        }
    }
}
