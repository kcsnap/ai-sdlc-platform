namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// GitHub webhook landed on the orchestrator.
/// </summary>
/// <param name="WebhookEndpoint">Endpoint that received the webhook (e.g. <c>/github/webhook</c>).</param>
/// <param name="Action">Dotted GitHub event/action pair (e.g. <c>issues.opened</c>, <c>issue_comment.created</c>).</param>
/// <param name="Summary">Human-readable summary of the webhook payload.</param>
/// <param name="IssueTitle">Issue title at time of webhook, if available.</param>
/// <param name="IssueState">Issue state at time of webhook (e.g. <c>open</c>, <c>closed</c>).</param>
public sealed record WebhookReceivedData(
    string WebhookEndpoint,
    string Action,
    string Summary,
    string? IssueTitle = null,
    string? IssueState = null) : EventData;
