using AiSdlc.Shared;

namespace AiSdlc.GitHub;

public interface IGitHubService
{
    Task<IssueDetails> GetIssueAsync(string repository, int issueNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string repository, int issueNumber, CancellationToken cancellationToken);
    Task<IssueComment> AddIssueCommentAsync(string repository, int issueNumber, string markdown, CancellationToken cancellationToken);
    Task<IssueComment> AddPullRequestCommentAsync(string repository, int pullRequestNumber, string markdown, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> AddLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> RemoveLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken);
    Task<GitHubPullRequestReference> CreatePullRequestAsync(CreatePullRequestRequest request, CancellationToken cancellationToken);
    Task<PullRequestDetails> GetPullRequestAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string repository, string reference, CancellationToken cancellationToken);

    /// <summary>Returns the decoded text content of a file, or null if not found.</summary>
    Task<string?> GetFileContentAsync(string repository, string path, CancellationToken cancellationToken);

    /// <summary>Returns the decoded text content of a file from a specific branch, or null if not found.</summary>
    Task<string?> GetBranchFileContentAsync(string repository, string path, string branch, CancellationToken cancellationToken);

    /// <summary>Lists every blob (path + size) in a branch's tree, recursively.</summary>
    Task<IReadOnlyList<RepoTreeEntry>> GetBranchFileTreeAsync(string repository, string branch, CancellationToken cancellationToken);

    Task MergePullRequestAsync(string repository, int pullRequestNumber, string commitMessage, CancellationToken cancellationToken);

    Task<string> GetDefaultBranchAsync(string repository, CancellationToken cancellationToken);
    Task<string> GetDefaultBranchShaAsync(string repository, string branch, CancellationToken cancellationToken);
    Task CreateBranchAsync(string repository, string branchName, string sha, CancellationToken cancellationToken);
    Task CreateOrUpdateFileAsync(string repository, string path, string content, string commitMessage, string branch, CancellationToken cancellationToken);

    /// <summary>Searches an organisation for open issues carrying the given label.</summary>
    Task<IReadOnlyList<OrgIssueSearchHit>> SearchOpenOrgIssuesByLabelAsync(string organisation, string label, CancellationToken cancellationToken);
}
