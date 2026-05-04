namespace AiSdlc.GitHub;

public sealed record IssueComment
{
    public required long CommentId { get; init; }
    public required string Repository { get; init; }
    public required int IssueOrPullRequestNumber { get; init; }
    public required string BodyMarkdown { get; init; }
    public required string AuthorLogin { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
