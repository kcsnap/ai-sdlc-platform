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

    /// <summary>Newest open PR whose head branch starts with the prefix, or null.</summary>
    Task<OpenPullRequestInfo?> GetNewestOpenPullRequestByBranchPrefixAsync(string repository, string branchPrefix, CancellationToken cancellationToken);
    Task<PullRequestDetails> GetPullRequestAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string repository, string reference, CancellationToken cancellationToken);

    /// <summary>Returns the decoded text content of a file, or null if not found.</summary>
    Task<string?> GetFileContentAsync(string repository, string path, CancellationToken cancellationToken);

    /// <summary>Returns the decoded text content of a file from a specific branch, or null if not found.</summary>
    Task<string?> GetBranchFileContentAsync(string repository, string path, string branch, CancellationToken cancellationToken);

    /// <summary>Lists every blob (path + size) in a branch's tree, recursively.</summary>
    Task<IReadOnlyList<RepoTreeEntry>> GetBranchFileTreeAsync(string repository, string branch, CancellationToken cancellationToken);

    /// <summary>
    /// For each FAILED check run on the commit: its annotations (preferred) or the Actions
    /// job-log tail (fallback). Per-check fetch errors degrade to an empty finding, never an
    /// exception — callers treat "no findings" as non-actionable.
    /// </summary>
    Task<IReadOnlyList<FailedCheckFinding>> GetFailedCheckFindingsAsync(string repository, string reference, CancellationToken cancellationToken);

    Task MergePullRequestAsync(string repository, int pullRequestNumber, string commitMessage, CancellationToken cancellationToken);

    Task<string> GetDefaultBranchAsync(string repository, CancellationToken cancellationToken);
    Task<string> GetDefaultBranchShaAsync(string repository, string branch, CancellationToken cancellationToken);
    Task CreateBranchAsync(string repository, string branchName, string sha, CancellationToken cancellationToken);
    Task CreateOrUpdateFileAsync(string repository, string path, string content, string commitMessage, string branch, CancellationToken cancellationToken);

    /// <summary>Searches an organisation for open issues carrying the given label.</summary>
    Task<IReadOnlyList<OrgIssueSearchHit>> SearchOpenOrgIssuesByLabelAsync(string organisation, string label, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new repository under <paramref name="targetOwner"/> from a template repo
    /// (<c>POST /repos/{template}/generate</c>). Requires App `Administration:write`.
    /// </summary>
    Task<CreatedRepository> CreateRepositoryFromTemplateAsync(
        string templateRepository, string targetOwner, string name, bool isPrivate, string description,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a repository Actions variable (used to wire deploy config — e.g. the OIDC
    /// client/tenant/subscription IDs the generated <c>deploy.yml</c> authenticates with).
    /// </summary>
    Task SetRepoVariableAsync(string repository, string name, string value, CancellationToken cancellationToken);
}
