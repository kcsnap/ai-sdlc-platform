using AiSdlc.Shared;

namespace AiSdlc.GitHub;

public sealed record IssueDetails
{
    public required GitHubIssueReference Issue { get; init; }
    public required string Title { get; init; }
    public required string BodyMarkdown { get; init; }
    public required string State { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public required string AuthorLogin { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
