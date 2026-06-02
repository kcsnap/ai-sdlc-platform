using AiSdlc.Audit;
using AiSdlc.Events.Contract;
using AiSdlc.Events.Contract.Data;
using AiSdlc.Orchestrator.Events;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class AuditEventMapperTests
{
    private const string Repo = "kcsnap/ai-sdlc-platform";
    private const string RunId = "kcsnap_ai-sdlc-platform_123";
    private const string SampleRowKey = "00637847651311234567_abc123def456";

    [Fact]
    public void Webhook_MapsToWebhookReceivedData_WithReferenceFields()
    {
        var stored = Make("Webhook", "/github/webhook", "issues.opened",
            "Issue #1 opened: Hello",
            references: new() { ["issueTitle"] = "Hello", ["issueState"] = "open" });

        var envelope = AuditEventMapper.TryMap(stored)!;
        var data = Assert.IsType<WebhookReceivedData>(envelope.Data);

        Assert.Equal(EventType.WebhookReceived, envelope.EventType);
        Assert.Equal("/github/webhook", data.WebhookEndpoint);
        Assert.Equal("issues.opened", data.Action);
        Assert.Equal("Hello", data.IssueTitle);
        Assert.Equal("open", data.IssueState);
    }

    [Fact]
    public void AgentStarted_MapsToAgentStartedData()
    {
        var stored = Make("Agent", "Architect", "Started", "Beginning");
        var envelope = AuditEventMapper.TryMap(stored)!;
        var data = Assert.IsType<AgentStartedData>(envelope.Data);

        Assert.Equal(EventType.AgentStarted, envelope.EventType);
        Assert.Equal("Architect", data.AgentName);
        Assert.Equal("Beginning", data.Summary);
    }

    [Fact]
    public void AgentCompleted_MapsAllStructuredFields()
    {
        var stored = Make("Agent", "Risk Assessor", "Completed", "Low risk",
            decision: "AUTO_MERGE_ELIGIBLE", riskLevel: "Low", commitSha: "deadbeef");

        var envelope = AuditEventMapper.TryMap(stored)!;
        var data = Assert.IsType<AgentCompletedData>(envelope.Data);

        Assert.Equal(EventType.AgentCompleted, envelope.EventType);
        Assert.Equal("Risk Assessor", data.AgentName);
        Assert.Equal("Low risk", data.Summary);
        Assert.Equal("AUTO_MERGE_ELIGIBLE", data.Decision);
        Assert.Equal("Low", data.RiskLevel);
        Assert.Equal("deadbeef", data.CommitSha);
    }

    [Fact]
    public void AgentFailed_ExtractsExceptionFieldsFromReferences()
    {
        var stored = Make("Agent", "Senior Coder", "Failed", "boom",
            references: new()
            {
                ["exceptionType"] = "System.InvalidOperationException",
                ["stackTrace"] = "at Foo.Bar() in C:\\x.cs:line 42"
            });

        var envelope = AuditEventMapper.TryMap(stored)!;
        var data = Assert.IsType<AgentFailedData>(envelope.Data);

        Assert.Equal(EventType.AgentFailed, envelope.EventType);
        Assert.Equal("System.InvalidOperationException", data.ExceptionType);
        Assert.Equal("at Foo.Bar() in C:\\x.cs:line 42", data.StackTrace);
    }

    [Fact]
    public void CommentPosted_RequiresCommentUrlAndCommentIdFromReferences()
    {
        var stored = Make("Comment", "GitHubComment", "Posted", "Architecture review",
            references: new() { ["commentUrl"] = "https://github.com/o/r/pull/1#c-99", ["commentId"] = "99" });

        var envelope = AuditEventMapper.TryMap(stored)!;
        var data = Assert.IsType<CommentPostedData>(envelope.Data);

        Assert.Equal(EventType.CommentPosted, envelope.EventType);
        Assert.Equal("https://github.com/o/r/pull/1#c-99", data.CommentUrl);
        Assert.Equal(99L, data.CommentId);
    }

    [Fact]
    public void CommentPosted_WithoutReferences_IsSkipped()
    {
        var stored = Make("Comment", "GitHubComment", "Posted", "no refs");
        Assert.Null(AuditEventMapper.TryMap(stored));
    }

    [Fact]
    public void WorkflowReleased_MapsCorrectly()
    {
        var stored = Make("Workflow", "Orchestrator", "Released", "PR merged");
        var envelope = AuditEventMapper.TryMap(stored)!;
        Assert.IsType<WorkflowReleasedData>(envelope.Data);
        Assert.Equal(EventType.WorkflowReleased, envelope.EventType);
    }

    [Fact]
    public void WorkflowStopped_CarriesDecision()
    {
        var stored = Make("Workflow", "Orchestrator", "Stopped", "Human stop", decision: "Stopped");
        var envelope = AuditEventMapper.TryMap(stored)!;
        var data = Assert.IsType<WorkflowStoppedData>(envelope.Data);
        Assert.Equal("Stopped", data.Decision);
    }

    [Fact]
    public void BootstrapTerminalMarker_RequiresValidStatusDecision()
    {
        var completed = Make("Workflow", "Orchestrator", "BootstrapTerminalMarker", "ok", decision: "completed");
        var failed = Make("Workflow", "Orchestrator", "BootstrapTerminalMarker", "fail", decision: "failed");
        var bogus = Make("Workflow", "Orchestrator", "BootstrapTerminalMarker", "x", decision: "weird-status");

        var completedData = Assert.IsType<BootstrapTerminalMarkerData>(AuditEventMapper.TryMap(completed)!.Data);
        var failedData = Assert.IsType<BootstrapTerminalMarkerData>(AuditEventMapper.TryMap(failed)!.Data);
        Assert.Equal("completed", completedData.Status);
        Assert.Equal("failed", failedData.Status);
        Assert.Null(AuditEventMapper.TryMap(bogus));
    }

    [Fact]
    public void UnknownActorType_ReturnsNull()
    {
        var stored = Make("FuturisticActorType", "x", "y", "z");
        Assert.Null(AuditEventMapper.TryMap(stored));
    }

    [Fact]
    public void Envelope_CarriesCursorFromRowKey()
    {
        var stored = Make("Agent", "Architect", "Started", "go");
        var envelope = AuditEventMapper.TryMap(stored)!;

        Assert.True(CursorCodec.TryDecode(envelope.Cursor, out var rowKey));
        Assert.Equal(SampleRowKey, rowKey);
    }

    private static StoredAuditEvent Make(
        string actorType,
        string actorName,
        string action,
        string summary,
        string? decision = null,
        string? riskLevel = null,
        string? commitSha = null,
        Dictionary<string, string>? references = null)
    {
        var auditEvent = new AuditEvent
        {
            RunId = RunId,
            TimestampUtc = new DateTimeOffset(2026, 6, 2, 14, 32, 11, 234, TimeSpan.Zero),
            Repository = Repo,
            IssueNumber = 123,
            PullRequestNumber = null,
            ActorType = actorType,
            ActorName = actorName,
            Action = action,
            Summary = summary,
            Decision = decision,
            RiskLevel = riskLevel,
            CommitSha = commitSha,
            RedactionApplied = false,
            References = references ?? new()
        };
        return new StoredAuditEvent(auditEvent, SampleRowKey);
    }
}
