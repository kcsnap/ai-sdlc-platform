namespace AiSdlc.Shared;

public sealed class AgentContext
{
    public required string RunId { get; init; }
    public required string Repository { get; init; }
    public required int IssueNumber { get; init; }
    public int? PullRequestNumber { get; init; }
    public required string CurrentState { get; init; }
    public required string RequestedAgent { get; init; }
    public Dictionary<string, string> ArtefactRefs { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
