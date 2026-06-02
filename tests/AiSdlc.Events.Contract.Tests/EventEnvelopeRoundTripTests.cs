using System.Text.Json;
using AiSdlc.Events.Contract;
using AiSdlc.Events.Contract.Data;
using Xunit;

namespace AiSdlc.Events.Contract.Tests;

public sealed class EventEnvelopeRoundTripTests
{
    private static readonly JsonSerializerOptions Options = EventStreamSerializer.Options;

    [Fact]
    public void WebhookReceived_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.WebhookReceived,
            new WebhookReceivedData(
                WebhookEndpoint: "/github/webhook",
                Action: "issues.opened",
                Summary: "Issue #42 opened by ada",
                IssueTitle: "Sample issue",
                IssueState: "open"));

        AssertRoundTrips<WebhookReceivedData>(envelope);
    }

    [Fact]
    public void WorkflowStarted_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.WorkflowStarted, new WorkflowStartedData());
        AssertRoundTrips<WorkflowStartedData>(envelope);
    }

    [Fact]
    public void AgentStarted_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.AgentStarted,
            new AgentStartedData(AgentName: "Architect", Summary: "Beginning analysis."));
        AssertRoundTrips<AgentStartedData>(envelope);
    }

    [Fact]
    public void AgentCompleted_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.AgentCompleted,
            new AgentCompletedData(
                AgentName: "Risk Assessor",
                Summary: "Low-risk change to docs.",
                Decision: "AUTO_MERGE_ELIGIBLE",
                RiskLevel: "Low",
                CommitSha: "abc1234"));
        AssertRoundTrips<AgentCompletedData>(envelope);
    }

    [Fact]
    public void AgentFailed_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.AgentFailed,
            new AgentFailedData(
                AgentName: "Senior Coder",
                Summary: "Compilation failed.",
                ExceptionType: "System.InvalidOperationException",
                StackTrace: "at Foo.Bar()..."));
        AssertRoundTrips<AgentFailedData>(envelope);
    }

    [Fact]
    public void CommentPosted_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.CommentPosted,
            new CommentPostedData(
                Summary: "Architecture review",
                CommentUrl: "https://github.com/o/r/pull/1#issuecomment-99",
                CommentId: 99L));
        AssertRoundTrips<CommentPostedData>(envelope);
    }

    [Fact]
    public void WorkflowReleased_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.WorkflowReleased,
            new WorkflowReleasedData(Summary: "PR merged, deployment recorded."));
        AssertRoundTrips<WorkflowReleasedData>(envelope);
    }

    [Fact]
    public void WorkflowStopped_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.WorkflowStopped,
            new WorkflowStoppedData(Summary: "Human requested stop.", Decision: "Stopped"));
        AssertRoundTrips<WorkflowStoppedData>(envelope);
    }

    [Fact]
    public void WorkflowFailed_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.WorkflowFailed,
            new WorkflowFailedData(Summary: "Orchestrator threw.", ExceptionType: "System.Exception"));
        AssertRoundTrips<WorkflowFailedData>(envelope);
    }

    [Fact]
    public void BootstrapTerminalMarker_Completed_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.BootstrapTerminalMarker,
            new BootstrapTerminalMarkerData(Status: "completed"));
        AssertRoundTrips<BootstrapTerminalMarkerData>(envelope);
    }

    [Fact]
    public void BootstrapTerminalMarker_Failed_RoundTrips()
    {
        var envelope = MakeEnvelope(EventType.BootstrapTerminalMarker,
            new BootstrapTerminalMarkerData(Status: "failed"));
        AssertRoundTrips<BootstrapTerminalMarkerData>(envelope);
    }

    private static EventEnvelope MakeEnvelope(EventType eventType, EventData data) => new()
    {
        Cursor = "MDAwMA",
        RunId = "kcsnap_ai-sdlc-platform_123",
        OccurredAt = new DateTimeOffset(2026, 6, 2, 14, 32, 11, 234, TimeSpan.Zero),
        EventType = eventType,
        Repository = "kcsnap/ai-sdlc-platform",
        IssueNumber = 123,
        PullRequestNumber = 124,
        RedactionApplied = false,
        Data = data,
    };

    private static void AssertRoundTrips<TData>(EventEnvelope original) where TData : EventData
    {
        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<EventEnvelope>(json, Options);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Cursor, roundTripped!.Cursor);
        Assert.Equal(original.RunId, roundTripped.RunId);
        Assert.Equal(original.OccurredAt, roundTripped.OccurredAt);
        Assert.Equal(original.EventType, roundTripped.EventType);
        Assert.Equal(original.Repository, roundTripped.Repository);
        Assert.Equal(original.IssueNumber, roundTripped.IssueNumber);
        Assert.Equal(original.PullRequestNumber, roundTripped.PullRequestNumber);
        Assert.Equal(original.RedactionApplied, roundTripped.RedactionApplied);
        Assert.IsType<TData>(roundTripped.Data);
        Assert.Equal(original.Data, roundTripped.Data);
    }
}
