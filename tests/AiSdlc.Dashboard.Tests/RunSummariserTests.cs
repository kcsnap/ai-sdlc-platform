using AiSdlc.Dashboard.Services;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Dashboard.Tests;

public sealed class RunSummariserTests
{
    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Empty(RunSummariser.Summarise(Array.Empty<DashboardEvent>()));
    }

    [Fact]
    public void GroupsEventsByRunId_AndCountsCorrectly()
    {
        var events = new[]
        {
            MakeEvent("run-a", 1, 0, "Webhook", "/github/webhook", "issues.opened",  "issue opened"),
            MakeAgent("run-a", 1, 1, "ProductStrategist", "Started",   "starting"),
            MakeAgent("run-a", 1, 2, "ProductStrategist", "Completed", "done"),
            MakeAgent("run-b", 2, 3, "BusinessAnalyst",   "Started",   "starting b")
        };

        var summaries = RunSummariser.Summarise(events);

        Assert.Equal(2, summaries.Count);
        // Sorted newest-first by latest activity timestamp
        Assert.Equal("run-b", summaries[0].RunId);
        Assert.Equal("run-a", summaries[1].RunId);

        var runA = summaries.Single(s => s.RunId == "run-a");
        Assert.Equal(3, runA.EventCount);
        Assert.Equal(1, runA.AgentCount);
    }

    [Fact]
    public void Status_Released_WhenReleaseManagerCompleted()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Completed", "ok"),
            MakeAgent("r", 1, 1, "ReleaseManager",     "Completed", "PR opened")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Released, summary.Status);
    }

    [Fact]
    public void Status_Released_TolerantOfFriendlyNames()
    {
        // The deployed orchestrator writes friendly names like "Release Manager" with spaces.
        var events = new[]
        {
            MakeAgent("r", 1, 0, "Release Manager", "Completed", "PR opened")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Released, summary.Status);
    }

    [Fact]
    public void Status_Failed_WhenLatestOutcomeForAnAgentIsFailed()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Started",   "starting"),
            MakeAgent("r", 1, 1, "ProductStrategist", "Failed",    "boom")
        };

        Assert.Equal(RunStatus.Failed, RunSummariser.Summarise(events).Single().Status);
    }

    [Fact]
    public void Status_Running_WhenFailedAttemptLaterRecovered()
    {
        // Transient failures that recovered via retry should not mark the run as Failed.
        var events = new[]
        {
            MakeAgent("r", 1, 0, "BusinessAnalyst", "Started",   "s1"),
            MakeAgent("r", 1, 1, "BusinessAnalyst", "Failed",    "transient 429"),
            MakeAgent("r", 1, 2, "BusinessAnalyst", "Started",   "s2"),
            MakeAgent("r", 1, 3, "BusinessAnalyst", "Completed", "ok on retry")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Running, summary.Status);
        Assert.Equal(1, summary.RetryCount);
        Assert.Equal(1, summary.FailedEventCount);
    }

    [Fact]
    public void Status_Stopped_WhenWorkflowStoppedEventExists()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Completed", "ok"),
            MakeWorkflowExit("r", 1, 1, "Stopped", "Code implementer produced no file changes")
        };

        Assert.Equal(RunStatus.Stopped, RunSummariser.Summarise(events).Single().Status);
    }

    [Fact]
    public void Status_Stopped_WinsOverRunningAgentActivity()
    {
        // Even when there are agent activity events, an orchestrator-side Stopped signal
        // takes precedence — the workflow won't make further progress.
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Started",   "starting"),
            MakeAgent("r", 1, 1, "ProductStrategist", "Completed", "ok"),
            MakeAgent("r", 1, 2, "BusinessAnalyst",   "Started",   "still going..."),
            MakeWorkflowExit("r", 1, 3, "Stopped", "Brief approval timed out")
        };

        Assert.Equal(RunStatus.Stopped, RunSummariser.Summarise(events).Single().Status);
    }

    [Fact]
    public void Status_Failed_FromOrchestratorEvent_TakesPrecedenceOverAgentFailures()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Completed", "ok"),
            MakeWorkflowExit("r", 1, 1, "Failed", "Risk assessment blocked the workflow")
        };

        Assert.Equal(RunStatus.Failed, RunSummariser.Summarise(events).Single().Status);
    }

    [Fact]
    public void Status_Pending_WhenOnlyWebhookEvents()
    {
        var events = new[]
        {
            MakeEvent("r", 7, 0, "Webhook", "/github/webhook", "issues.opened", "hi")
        };

        Assert.Equal(RunStatus.Pending, RunSummariser.Summarise(events).Single().Status);
    }

    [Fact]
    public void PullRequestNumber_IsCapturedFromLatestEventWithOne()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Started",   "s"),
            MakeAgent("r", 1, 1, "ReleaseManager",    "Completed", "pr opened", pr: 42)
        };

        Assert.Equal(42, RunSummariser.Summarise(events).Single().PullRequestNumber);
    }

    [Fact]
    public void IssueTitle_PreferredFromReferences_FallsBackToSummaryParsing()
    {
        // Modern path: webhook writes structured References["issueTitle"]
        var modern = MakeWebhookOpenedWithReferences(28, title: "Add dark mode toggle");
        Assert.Equal("Add dark mode toggle", RunSummariser.Summarise(new[] { modern }).Single().IssueTitle);

        // Legacy path: no References, fall back to parsing the formatted Summary string
        var legacy = MakeWebhookOpenedLegacySummary(99, title: "Refactor auth middleware");
        Assert.Equal("Refactor auth middleware", RunSummariser.Summarise(new[] { legacy }).Single().IssueTitle);
    }

    [Fact]
    public void IssueTitle_PicksFromEarliestWebhookEvent()
    {
        // A later 'edited' webhook with a different title shouldn't overwrite the originally-opened title.
        var events = new[]
        {
            MakeWebhookOpenedWithReferences(7, title: "Original title", offset: 0),
            MakeWebhookEditedWithReferences(7, title: "Edited later",   offset: 100),
            MakeAgent("org_repo_7", 7, 50, "ProductStrategist", "Completed", "ok")
        };

        Assert.Equal("Original title", RunSummariser.Summarise(events).Single().IssueTitle);
    }

    [Fact]
    public void IssueUrl_BuildsCorrectGitHubUrl()
    {
        var summary = RunSummariser.Summarise(new[] { MakeWebhookOpenedWithReferences(28, "x") }).Single();
        Assert.Equal("https://github.com/org/repo/issues/28", summary.IssueUrl);
    }

    [Fact]
    public void IssueState_PicksLatestWebhookEventSoCloseFollowsOpen()
    {
        var events = new[]
        {
            MakeWebhookWithState(7, action: "opened", title: "t", state: "open", reason: null, offset: 0),
            MakeWebhookWithState(7, action: "closed", title: "t", state: "closed", reason: "not_planned", offset: 100)
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal("closed",       summary.IssueState);
        Assert.Equal("not_planned",  summary.IssueStateReason);
        Assert.Equal("Closed as not planned", summary.IssueStateLabel);
    }

    [Fact]
    public void IssueState_ReopenAfterCloseReflectsOpenAgain()
    {
        var events = new[]
        {
            MakeWebhookWithState(7, action: "opened",   title: "t", state: "open",   reason: null,        offset: 0),
            MakeWebhookWithState(7, action: "closed",   title: "t", state: "closed", reason: "completed", offset: 100),
            MakeWebhookWithState(7, action: "reopened", title: "t", state: "open",   reason: "reopened",  offset: 200)
        };

        var summary = RunSummariser.Summarise(events).Single();
        // After reopen GitHub sets state back to "open" — chip shows "Open" again, not "Closed".
        Assert.Equal("open", summary.IssueState);
        Assert.Equal("Open", summary.IssueStateLabel);
    }

    [Fact]
    public void IssueStateLabel_FormatsKnownReasons()
    {
        Assert.Equal("Closed as completed",
            RunSummariser.Summarise(new[] { MakeWebhookWithState(1, "closed", "t", "closed", "completed") }).Single().IssueStateLabel);
        Assert.Equal("Closed as duplicate",
            RunSummariser.Summarise(new[] { MakeWebhookWithState(2, "closed", "t", "closed", "duplicate") }).Single().IssueStateLabel);
        Assert.Equal("Closed",
            RunSummariser.Summarise(new[] { MakeWebhookWithState(3, "closed", "t", "closed", null) }).Single().IssueStateLabel);
        Assert.Equal("Open",
            RunSummariser.Summarise(new[] { MakeWebhookWithState(4, "opened", "t", "open", null) }).Single().IssueStateLabel);
    }

    [Fact]
    public void IssueState_NullWhenNoWebhookCarriedIt()
    {
        // Legacy data without state/state_reason in References.
        var legacy = MakeWebhookOpenedLegacySummary(99, title: "legacy");
        var summary = RunSummariser.Summarise(new[] { legacy }).Single();
        Assert.Null(summary.IssueState);
        Assert.Null(summary.IssueStateLabel);
    }

    [Fact]
    public void RetryCount_CountsFailedEventsLaterRecovered()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ContentSeoReviewer", "Started",   "s1"),
            MakeAgent("r", 1, 1, "ContentSeoReviewer", "Failed",    "a"),
            MakeAgent("r", 1, 2, "ContentSeoReviewer", "Started",   "s2"),
            MakeAgent("r", 1, 3, "ContentSeoReviewer", "Failed",    "b"),
            MakeAgent("r", 1, 4, "ContentSeoReviewer", "Started",   "s3"),
            MakeAgent("r", 1, 5, "ContentSeoReviewer", "Completed", "ok"),
            // Different agent with one unrecovered failure
            MakeAgent("r", 1, 6, "QaTestEngineer",     "Failed",    "blocked")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(2, summary.RetryCount);                    // CSR's two recovered failures
        Assert.Equal(3, summary.FailedEventCount);              // CSR x2 + QA x1
        Assert.Equal(RunStatus.Failed, summary.Status);         // QA's failure is unresolved
    }

    // ── Blocked status: Workflow.Awaiting* events without later resolution ────────

    [Fact]
    public void Status_Blocked_WhenAwaitEventHasNoLaterAgentActivity()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Completed", "ok"),
            MakeAgent("r", 1, 1, "ProductOwner",      "Completed", "brief drafted"),
            MakeWorkflowExit("r", 1, 2, "AwaitingBriefApproval", "Awaiting /approve-brief")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Blocked, summary.Status);
    }

    [Fact]
    public void Status_NotBlocked_WhenAgentActivityFollowsAwait()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductOwner",      "Completed", "brief"),
            MakeWorkflowExit("r", 1, 1, "AwaitingBriefApproval", "Awaiting /approve-brief"),
            MakeAgent("r", 1, 2, "BusinessAnalyst",   "Started",   "/approve-brief received, resuming")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Running, summary.Status);
    }

    [Fact]
    public void Status_Stopped_BeatsBlocked_WhenTimeoutExitFollowsAwait()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductOwner", "Completed", "brief"),
            MakeWorkflowExit("r", 1, 1, "AwaitingBriefApproval", "Awaiting /approve-brief"),
            MakeWorkflowExit("r", 1, 2, "Stopped", "Brief approval timed out")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Stopped, summary.Status);
    }

    [Fact]
    public void Status_Released_BeatsBlocked_WhenReleaseManagerCompletes()
    {
        var events = new[]
        {
            MakeWorkflowExit("r", 1, 0, "AwaitingMergeApproval", "Awaiting /approve-merge"),
            MakeAgent("r", 1, 1, "ReleaseManager", "Completed", "merged after approval")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Released, summary.Status);
    }

    [Fact]
    public void Status_Blocked_HandlesMergeApprovalGate()
    {
        var events = new[]
        {
            MakeAgent("r", 1, 0, "ProductStrategist", "Completed", "ok"),
            MakeAgent("r", 1, 1, "RiskAssessor",      "Completed", "AUTO_MERGE_ELIGIBLE"),
            MakeWorkflowExit("r", 1, 2, "AwaitingMergeApproval", "Awaiting /approve-merge (1 gate failure(s))")
        };

        var summary = RunSummariser.Summarise(events).Single();
        Assert.Equal(RunStatus.Blocked, summary.Status);
    }

    private static DashboardEvent MakeAgent(string runId, int issue, int offset, string agent, string action, string summary, int? pr = null) =>
        MakeEvent(runId, issue, offset, "Agent", agent, action, summary, pr);

    private static DashboardEvent MakeWorkflowExit(string runId, int issue, int offset, string outcome, string reason) =>
        MakeEvent(runId, issue, offset, "Workflow", "Orchestrator", outcome, reason);

    private static DashboardEvent MakeEvent(string runId, int issue, int offset, string actorType, string actor, string action, string summary, int? pr = null)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero);
        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId             = runId,
            TimestampUtc      = baseTs.AddSeconds(offset),
            Repository        = "org/repo",
            IssueNumber       = issue,
            PullRequestNumber = pr,
            ActorType         = actorType,
            ActorName         = actor,
            Action            = action,
            Summary           = summary
        });
    }

    private static DashboardEvent MakeWebhookOpenedWithReferences(int issue, string title, int offset = 0)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero);
        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId        = $"org_repo_{issue}",
            TimestampUtc = baseTs.AddSeconds(offset),
            Repository   = "org/repo",
            IssueNumber  = issue,
            ActorType    = "Webhook",
            ActorName    = "/github/webhook",
            Action       = "issues.opened",
            Summary      = $"Issue #{issue} opened: {title}",
            References   = new Dictionary<string, string> { ["issueTitle"] = title }
        });
    }

    private static DashboardEvent MakeWebhookEditedWithReferences(int issue, string title, int offset = 0)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero);
        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId        = $"org_repo_{issue}",
            TimestampUtc = baseTs.AddSeconds(offset),
            Repository   = "org/repo",
            IssueNumber  = issue,
            ActorType    = "Webhook",
            ActorName    = "/github/webhook",
            Action       = "issues.edited",
            Summary      = $"Issue #{issue} edited: {title}",
            References   = new Dictionary<string, string> { ["issueTitle"] = title }
        });
    }

    private static DashboardEvent MakeWebhookWithState(int issue, string action, string title, string state, string? reason, int offset = 0)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero);
        var refs = new Dictionary<string, string>
        {
            ["issueTitle"] = title,
            ["issueState"] = state
        };
        if (!string.IsNullOrWhiteSpace(reason)) refs["issueStateReason"] = reason!;

        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId        = $"org_repo_{issue}",
            TimestampUtc = baseTs.AddSeconds(offset),
            Repository   = "org/repo",
            IssueNumber  = issue,
            ActorType    = "Webhook",
            ActorName    = "/github/webhook",
            Action       = $"issues.{action}",
            Summary      = $"Issue #{issue} {action}: {title}",
            References   = refs
        });
    }

    private static DashboardEvent MakeWebhookOpenedLegacySummary(int issue, string title, int offset = 0)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero);
        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId        = $"org_repo_{issue}",
            TimestampUtc = baseTs.AddSeconds(offset),
            Repository   = "org/repo",
            IssueNumber  = issue,
            ActorType    = "Webhook",
            ActorName    = "/github/webhook",
            Action       = "issues.opened",
            Summary      = $"Issue #{issue} opened: {title}"
            // No References on purpose — exercises the legacy parse fallback.
        });
    }
}
