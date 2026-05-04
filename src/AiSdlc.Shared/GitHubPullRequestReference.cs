namespace AiSdlc.Shared;

/// <summary>
/// Represents a GitHub pull request created or reviewed by the workflow.
/// </summary>
public sealed record GitHubPullRequestReference(
    string Repository,
    int PullRequestNumber,
    string BranchName,
    string Url);
