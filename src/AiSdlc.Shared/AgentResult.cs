namespace AiSdlc.Shared;

public sealed class AgentResult
{
    public required string AgentName { get; init; }
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public string? OutputMarkdown { get; init; }
    public string? ContextRef { get; init; }
    public string? Decision { get; init; }
    public string? RiskLevel { get; init; }
    public List<string> ArtefactsCreated { get; init; } = new();
    public List<string> FollowUpQuestions { get; init; } = new();
    public List<string> BlockingIssues { get; init; } = new();
}
