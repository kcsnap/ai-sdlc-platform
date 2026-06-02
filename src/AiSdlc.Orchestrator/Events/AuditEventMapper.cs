using AiSdlc.Audit;
using AiSdlc.Events.Contract;
using AiSdlc.Events.Contract.Data;
using AiSdlc.Shared;

namespace AiSdlc.Orchestrator.Events;

/// <summary>
/// Maps a stored <see cref="AuditEvent"/> to the ADR-0004 <see cref="EventEnvelope"/> shape.
/// Returns <c>null</c> for audit events whose ActorType+Action combination is not yet in the v1 taxonomy —
/// the function skips those rather than emitting <see cref="EventType.Unknown"/> at the server side.
/// </summary>
internal static class AuditEventMapper
{
    public static EventEnvelope? TryMap(StoredAuditEvent stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var audit = stored.Event;
        var (eventType, data) = MapData(audit);
        if (data is null)
        {
            return null;
        }

        return new EventEnvelope
        {
            Cursor = CursorCodec.Encode(stored.RowKey),
            RunId = audit.RunId,
            OccurredAt = audit.TimestampUtc,
            EventType = eventType,
            Repository = audit.Repository,
            IssueNumber = audit.IssueNumber,
            PullRequestNumber = audit.PullRequestNumber,
            RedactionApplied = audit.RedactionApplied,
            Data = data,
        };
    }

    private static (EventType, EventData?) MapData(AuditEvent audit) => audit.ActorType switch
    {
        "Webhook" => (EventType.WebhookReceived, MapWebhook(audit)),
        "Agent" => MapAgent(audit),
        "Comment" when audit.Action == "Posted" => (EventType.CommentPosted, MapCommentPosted(audit)),
        "Workflow" => MapWorkflow(audit),
        _ => (EventType.Unknown, null),
    };

    private static EventData MapWebhook(AuditEvent audit) => new WebhookReceivedData(
        WebhookEndpoint: audit.ActorName,
        Action: audit.Action,
        Summary: audit.Summary,
        IssueTitle: audit.References.TryGetValue("issueTitle", out var title) ? title : null,
        IssueState: audit.References.TryGetValue("issueState", out var state) ? state : null);

    private static (EventType, EventData?) MapAgent(AuditEvent audit) => audit.Action switch
    {
        "Started" => (EventType.AgentStarted, new AgentStartedData(audit.ActorName, audit.Summary)),
        "Completed" => (EventType.AgentCompleted, new AgentCompletedData(
            AgentName: audit.ActorName,
            Summary: audit.Summary,
            Decision: audit.Decision,
            RiskLevel: audit.RiskLevel,
            CommitSha: audit.CommitSha)),
        "Failed" => (EventType.AgentFailed, new AgentFailedData(
            AgentName: audit.ActorName,
            Summary: audit.Summary,
            ExceptionType: audit.References.TryGetValue("exceptionType", out var et) ? et : null,
            StackTrace: audit.References.TryGetValue("stackTrace", out var st) ? st : null)),
        _ => (EventType.Unknown, null),
    };

    private static EventData? MapCommentPosted(AuditEvent audit)
    {
        if (!audit.References.TryGetValue("commentUrl", out var commentUrl) ||
            !audit.References.TryGetValue("commentId", out var commentIdString) ||
            !long.TryParse(commentIdString, out var commentId))
        {
            return null;
        }

        return new CommentPostedData(audit.Summary, commentUrl, commentId);
    }

    private static (EventType, EventData?) MapWorkflow(AuditEvent audit) => audit.Action switch
    {
        "Released" => (EventType.WorkflowReleased, new WorkflowReleasedData(audit.Summary)),
        "Stopped" => (EventType.WorkflowStopped, new WorkflowStoppedData(audit.Summary, audit.Decision)),
        "Failed" => (EventType.WorkflowFailed, new WorkflowFailedData(
            Summary: audit.Summary,
            ExceptionType: audit.References.TryGetValue("exceptionType", out var et) ? et : null)),
        "Started" => (EventType.WorkflowStarted, new WorkflowStartedData()),
        "BootstrapTerminalMarker" when audit.Decision is "completed" or "failed"
            => (EventType.BootstrapTerminalMarker, new BootstrapTerminalMarkerData(audit.Decision)),
        _ => (EventType.Unknown, null),
    };
}
