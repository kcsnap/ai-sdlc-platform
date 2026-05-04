namespace AiSdlc.Shared;

/// <summary>
/// Represents the GitHub issue that initiated or is associated with a workflow run.
/// </summary>
public sealed record GitHubIssueReference(
    string Repository,
    int IssueNumber,
    string Url);
