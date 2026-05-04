namespace AiSdlc.Shared;

public sealed class AuditEvent
{
    public required string RunId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string Repository { get; init; }
    public required int IssueNumber { get; init; }
    public int? PullRequestNumber { get; init; }
    public required string ActorType { get; init; }
    public required string ActorName { get; init; }
    public required string Action { get; init; }
    public required string Summary { get; init; }
    public string? Decision { get; init; }
    public string? RiskLevel { get; init; }
    public string? CommitSha { get; init; }
    public bool RedactionApplied { get; init; }
    public Dictionary<string, string> References { get; init; } = new();
}
