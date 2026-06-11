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
/// Self-healing net under the webhook intake. Periodically scans the configured GitHub
/// organisation for open issues still labeled ai-sdlc:bootstrap and rescues two failure modes:
/// 1. No orchestration record at all — the delivery was lost end-to-end (missing repo webhook,
///    dropped 5xx delivery, queue poison). The run is started fresh.
/// 2. An orchestration record in runtime status Failed — the run crashed silently mid-chain
///    (a graceful business failure posts a terminal marker and completes, so Failed means no
///    one was ever told). The run is restarted under the same instance ID, at most
///    MaxSilentFailureRestarts times, after which the failure is surfaced loudly instead.
/// Runs in status Running are never touched: they may legitimately be waiting on human approval.
/// Disabled unless the ReconciliationOrg app setting is present.
/// </summary>
public sealed class ReconciliationSweepFunction
{
    // Fresh issues/instances are skipped — a webhook delivery may legitimately still be in
    // flight, or an operator may be mid-way through a manual re-fire.
    internal static readonly TimeSpan FreshIssueGrace = TimeSpan.FromMinutes(10);

    // Every restart re-runs the full agent chain (real model spend), so silently-crashed runs
    // get a bounded number of second chances before the failure is surfaced instead.
    internal const int MaxSilentFailureRestarts = 2;

    internal const string RestartCountMetadataKey = "reconciliationRestarts";

    // Applied when restarts are exhausted. As a non-bootstrap ai-sdlc:* label it also blocks
    // the fresh-start path (ShouldReconcile), so a given-up issue can never re-enter the loop.
    internal const string ExhaustedLabel = "ai-sdlc:reconciliation-exhausted";

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
            try
            {
                if (await ReconcileIssueAsync(hit, durableClient, cancellationToken))
                    started++;
            }
            catch (Exception ex)
            {
                // One unreconcilable issue must not starve the rest of the sweep.
                _logger.LogError(ex, "Reconciliation failed for {Repository}#{Number} — continuing sweep.",
                    hit.Repository, hit.Number);
            }
        }

        if (started > 0)
            _logger.LogInformation("Reconciliation sweep started {Count} run(s) in {Org}.", started, organisation);
    }

    private async Task<bool> ReconcileIssueAsync(
        OrgIssueSearchHit hit, DurableTaskClient durableClient, CancellationToken cancellationToken)
    {
        var instanceId = GitHubWebhookProcessor.BuildInstanceId(hit.Repository, hit.Number);
        var existing   = await durableClient.GetInstanceAsync(
            instanceId, getInputsAndOutputs: true, cancellation: cancellationToken);

        if (existing is null)
        {
            if (!ShouldReconcile(hit.Labels, hit.UpdatedAt, DateTimeOffset.UtcNow))
                return false;

            await StartAsync(hit, instanceId, restartAttempt: 0, durableClient, cancellationToken);
            await WriteSweepAuditAsync(hit, instanceId, "reconciliation.started",
                $"Started stranded bootstrap issue #{hit.Number}: {hit.Title}", cancellationToken);
            _logger.LogWarning(
                "Reconciliation started orchestration {InstanceId} for stranded bootstrap issue {Repository}#{Number} — " +
                "its issues.opened delivery never arrived.", instanceId, hit.Repository, hit.Number);
            return true;
        }

        if (!ShouldRestartSilentFailure(existing.RuntimeStatus, existing.LastUpdatedAt, DateTimeOffset.UtcNow))
            return false;

        if (hit.Labels.Any(l => l.Equals(ExhaustedLabel, StringComparison.OrdinalIgnoreCase)))
        {
            // A previous give-up labeled the issue but crashed before purging — finish the job
            // without re-posting comments.
            await durableClient.PurgeInstanceAsync(instanceId, cancellation: cancellationToken);
            return false;
        }

        var attempts = ReadRestartCount(TryReadInput(existing));
        if (attempts >= MaxSilentFailureRestarts)
        {
            await GiveUpAsync(hit, instanceId, attempts, durableClient, cancellationToken);
            return false;
        }

        await durableClient.PurgeInstanceAsync(instanceId, cancellation: cancellationToken);
        await StartAsync(hit, instanceId, restartAttempt: attempts + 1, durableClient, cancellationToken);
        await WriteSweepAuditAsync(hit, instanceId, "reconciliation.restarted",
            $"Restarted silently-failed run for issue #{hit.Number} (attempt {attempts + 1} of {MaxSilentFailureRestarts}).",
            cancellationToken);
        _logger.LogWarning(
            "Reconciliation restarted silently-failed orchestration {InstanceId} for {Repository}#{Number} " +
            "(attempt {Attempt} of {Max}).", instanceId, hit.Repository, hit.Number, attempts + 1, MaxSilentFailureRestarts);
        return true;
    }

    private static async Task StartAsync(
        OrgIssueSearchHit hit, string instanceId, int restartAttempt,
        DurableTaskClient durableClient, CancellationToken cancellationToken)
    {
        var mode = GitHubWebhookProcessor.ResolveWorkflowMode(
            hit.Labels.Select(l => new WebhookLabel { Name = l }));

        var agentContext = GitHubWebhookProcessor.BuildAgentContext(
            instanceId, hit.Repository, hit.Number, mode,
            hit.Title, hit.Body, hit.Url, hit.Author);

        if (restartAttempt > 0)
            agentContext.Metadata[RestartCountMetadataKey] = restartAttempt.ToString();

        await durableClient.ScheduleNewOrchestrationInstanceAsync(
            nameof(AiSdlcWorkflowOrchestrator),
            agentContext,
            new StartOrchestrationOptions { InstanceId = instanceId },
            cancellationToken);
    }

    private async Task GiveUpAsync(
        OrgIssueSearchHit hit, string instanceId, int attempts,
        DurableTaskClient durableClient, CancellationToken cancellationToken)
    {
        // Label first: it blocks both sweep paths, so a crash anywhere below cannot loop or
        // double-post — the labeled-but-unpurged case is finished silently on the next sweep.
        await _gitHub.AddLabelsAsync(hit.Repository, hit.Number, [ExhaustedLabel], cancellationToken);
        await _gitHub.AddIssueCommentAsync(hit.Repository, hit.Number,
            BuildRestartsExhaustedComment(attempts), cancellationToken);
        await _gitHub.AddIssueCommentAsync(hit.Repository, hit.Number,
            AiSdlcWorkflowOrchestrator.TerminalStatusMarkerFailed, cancellationToken);

        // Typed counterpart of the HTML-comment marker (ADR-0004) — the events API consumes this.
        await WriteSweepAuditAsync(hit, instanceId, "BootstrapTerminalMarker",
            "Bootstrap run failed.", cancellationToken, decision: "failed");
        await WriteSweepAuditAsync(hit, instanceId, "reconciliation.exhausted",
            $"Gave up on issue #{hit.Number} after {attempts} automatic restart(s) — run keeps crashing silently.",
            cancellationToken);

        await durableClient.PurgeInstanceAsync(instanceId, cancellation: cancellationToken);
        _logger.LogError(
            "Reconciliation gave up on {InstanceId} for {Repository}#{Number} after {Attempts} restart(s) — " +
            "marked failed.", instanceId, hit.Repository, hit.Number, attempts);
    }

    /// <summary>
    /// A stranded issue qualifies for a fresh start when it is old enough that its webhook
    /// delivery cannot still be in flight, and carries no ai-sdlc progression label beyond
    /// bootstrap — progression labels prove a run already handled it (covers completed runs
    /// whose instances were purged by a later close/reopen).
    /// </summary>
    public static bool ShouldReconcile(IReadOnlyList<string> labels, DateTimeOffset updatedAt, DateTimeOffset nowUtc)
    {
        if (nowUtc - updatedAt < FreshIssueGrace)
            return false;

        return !labels.Any(l =>
            l.StartsWith("ai-sdlc:", StringComparison.OrdinalIgnoreCase)
            && !l.Equals(GitHubWebhookProcessor.BootstrapLabel, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Only runtime status Failed marks a silent mid-chain crash: graceful business failures
    /// post a terminal marker and complete (status Completed), Running may be waiting on a
    /// human, and Terminated/Suspended are deliberate operator actions.
    /// </summary>
    public static bool ShouldRestartSilentFailure(
        OrchestrationRuntimeStatus status, DateTimeOffset lastUpdatedAt, DateTimeOffset nowUtc) =>
        status == OrchestrationRuntimeStatus.Failed && nowUtc - lastUpdatedAt >= FreshIssueGrace;

    internal static int ReadRestartCount(AgentContext? input)
    {
        if (input is null || !input.Metadata.TryGetValue(RestartCountMetadataKey, out var value))
            return 0;

        // Metadata values round-trip through Durable serialization as JsonElement — parse via text.
        return int.TryParse(value?.ToString(), out var count) && count > 0 ? count : 0;
    }

    internal static string BuildRestartsExhaustedComment(int attempts) =>
        "## AI SDLC — Run Failed (auto-recovery exhausted)\n\n" +
        $"This run crashed mid-chain and was automatically restarted {attempts} time(s) by the " +
        "reconciliation sweep, but crashed again each time.\n\n" +
        "The build is now marked failed. Once the underlying problem is resolved, remove the " +
        $"`{ExhaustedLabel}` label and close/reopen this issue to start again from the top.";

    private static AgentContext? TryReadInput(OrchestrationMetadata metadata)
    {
        try
        {
            return metadata.ReadInputAs<AgentContext>();
        }
        catch (Exception)
        {
            // Unreadable input (old schema, manual start) — treat as a first restart.
            return null;
        }
    }

    private async Task WriteSweepAuditAsync(
        OrgIssueSearchHit hit, string instanceId, string action, string summary,
        CancellationToken cancellationToken, string? decision = null)
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
                Action      = action,
                Summary     = summary,
                Decision    = decision
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit writes must never break the sweep.
            _logger.LogWarning(ex, "Failed to write reconciliation audit event for {InstanceId}.", instanceId);
        }
    }
}
