namespace AiSdlc.GitHub;

public sealed record CreatePullRequestRequest
{
    public required string Repository { get; init; }
    public required string Title { get; init; }
    public required string BodyMarkdown { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public bool Draft { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
}
