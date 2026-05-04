using AiSdlc.Shared;

namespace AiSdlc.GitHub;

public sealed record PullRequestDetails
{
    public required GitHubPullRequestReference PullRequest { get; init; }
    public required string Title { get; init; }
    public required string BodyMarkdown { get; init; }
    public required string State { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string AuthorLogin { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public bool Draft { get; init; }
    public bool Mergeable { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
