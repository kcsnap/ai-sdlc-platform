using AiSdlc.Dashboard.Services;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Dashboard.Tests;

public sealed class FeedGrouperTests
{
    [Fact]
    public void Empty_ReturnsEmpty()
    {
        var rows = FeedGrouper.Group(Array.Empty<DashboardEvent>());
        Assert.Empty(rows);
    }

    [Fact]
    public void NonAgentEvents_PassThroughUntouched()
    {
        var webhook = MakeEvent(0, "Webhook", "/github/webhook", "issues.opened", "ok");
        var system  = MakeEvent(1, "System",  "Orchestrator",    "RunStarted",   "started");

        var rows = FeedGrouper.Group(new[] { webhook, system });

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Empty(r.PriorAttempts));
    }

    [Fact]
    public void StartedThenCompleted_NoRetries_FoldsToSingleCompletedRow()
    {
        var start = MakeAgent(0, "BusinessAnalyst", "Started",   "starting");
        var done  = MakeAgent(1, "BusinessAnalyst", "Completed", "all good");

        var rows = FeedGrouper.Group(new[] { start, done });

        Assert.Single(rows);
        Assert.Equal("Completed", rows[0].Display.Action);
        Assert.Empty(rows[0].PriorAttempts);
    }

    [Fact]
    public void RetriedAgent_DisplaysFinalCompletedAndFoldsPriorFailuresIntoDrilldown()
    {
        // Started, Failed, Started, Failed, Started, Completed — exactly the user's scenario.
        var events = new[]
        {
            MakeAgent(0, "ContentSeoReviewer", "Started",   "first start"),
            MakeAgent(1, "ContentSeoReviewer", "Failed",    "429 rate limit (a)"),
            MakeAgent(2, "ContentSeoReviewer", "Started",   "retry 1 start"),
            MakeAgent(3, "ContentSeoReviewer", "Failed",    "429 rate limit (b)"),
            MakeAgent(4, "ContentSeoReviewer", "Started",   "retry 2 start"),
            MakeAgent(5, "ContentSeoReviewer", "Completed", "succeeded on third try")
        };

        var rows = FeedGrouper.Group(events);

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("Completed", row.Display.Action);
        Assert.Equal("succeeded on third try", row.Display.Summary);

        // Two prior Failed attempts, oldest first
        Assert.Equal(2, row.PriorAttempts.Count);
        Assert.Equal("429 rate limit (a)", row.PriorAttempts[0].Summary);
        Assert.Equal("429 rate limit (b)", row.PriorAttempts[1].Summary);
        Assert.True(row.HasPriorAttempts);
        Assert.Equal(3, row.AttemptCount);
    }

    [Fact]
    public void AllAttemptsFailed_DisplaysLatestFailedAndFoldsEarlierFailures()
    {
        var events = new[]
        {
            MakeAgent(0, "QaTestEngineer", "Started", "a"),
            MakeAgent(1, "QaTestEngineer", "Failed",  "attempt 1 failed"),
            MakeAgent(2, "QaTestEngineer", "Started", "b"),
            MakeAgent(3, "QaTestEngineer", "Failed",  "attempt 2 failed"),
            MakeAgent(4, "QaTestEngineer", "Started", "c"),
            MakeAgent(5, "QaTestEngineer", "Failed",  "attempt 3 failed — final")
        };

        var rows = FeedGrouper.Group(events);

        Assert.Single(rows);
        Assert.Equal("Failed", rows[0].Display.Action);
        Assert.Equal("attempt 3 failed — final", rows[0].Display.Summary);
        Assert.Equal(2, rows[0].PriorAttempts.Count);
    }

    [Fact]
    public void TwoSeparateInvocationsOfSameAgent_StayAsTwoGroups()
    {
        // Workflow legitimately calls the BusinessAnalyst twice (e.g. initial review then re-review).
        // The Completed event closes the first execution; a later Started begins a new one.
        var events = new[]
        {
            MakeAgent(0,  "BusinessAnalyst", "Started",   "first run start"),
            MakeAgent(1,  "BusinessAnalyst", "Completed", "first run done"),
            MakeAgent(10, "BusinessAnalyst", "Started",   "second run start"),
            MakeAgent(11, "BusinessAnalyst", "Completed", "second run done")
        };

        var rows = FeedGrouper.Group(events);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Empty(r.PriorAttempts));
        // Newest first in display order
        Assert.Equal("second run done", rows[0].Display.Summary);
        Assert.Equal("first run done",  rows[1].Display.Summary);
    }

    [Fact]
    public void OnlyStartedSoFar_DisplaysStartedAsRow()
    {
        var events = new[] { MakeAgent(0, "Architect", "Started", "in flight") };
        var rows = FeedGrouper.Group(events);

        Assert.Single(rows);
        Assert.Equal("Started", rows[0].Display.Action);
        Assert.Empty(rows[0].PriorAttempts);
    }

    [Fact]
    public void OutputOrderedNewestFirst()
    {
        var events = new[]
        {
            MakeEvent(0, "Webhook", "/github/webhook", "issues.opened",  "early webhook"),
            MakeAgent(5, "Architect", "Started",   "arch start"),
            MakeAgent(6, "Architect", "Completed", "arch done"),
            MakeEvent(7, "System",  "Orchestrator", "RunCompleted", "all done")
        };

        var rows = FeedGrouper.Group(events);

        Assert.Equal(3, rows.Count);
        // Newest first
        Assert.Equal("RunCompleted",  rows[0].Display.Action);
        Assert.Equal("Completed",     rows[1].Display.Action);  // Architect execution (display = Completed at t=6)
        Assert.Equal("issues.opened", rows[2].Display.Action);
    }

    private static DashboardEvent MakeAgent(int offsetSec, string agent, string action, string summary) =>
        MakeEvent(offsetSec, "Agent", agent, action, summary);

    private static DashboardEvent MakeEvent(int offsetSec, string actorType, string actor, string action, string summary)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 13, 51, 20, TimeSpan.Zero);
        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId        = "run-1",
            TimestampUtc = baseTs.AddSeconds(offsetSec),
            Repository   = "launchcart/launchcart",
            IssueNumber  = 402,
            ActorType    = actorType,
            ActorName    = actor,
            Action       = action,
            Summary      = summary
        });
    }
}
