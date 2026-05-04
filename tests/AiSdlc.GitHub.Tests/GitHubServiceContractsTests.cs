using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubServiceContractsTests
{
    [Fact]
    public void CreatePullRequestRequest_ShouldCaptureExpectedFields()
    {
        var request = new CreatePullRequestRequest
        {
            Repository = "kcsnap/ai-sdlc-platform",
            Title = "Add deterministic risk engine",
            BodyMarkdown = "Implements Phase 2 contracts.",
            HeadBranch = "ai/002-risk-rules-engine",
            BaseBranch = "main",
            Draft = true,
            Labels = new[] { "automation", "risk" }
        };

        Assert.Equal("kcsnap/ai-sdlc-platform", request.Repository);
        Assert.Equal("ai/002-risk-rules-engine", request.HeadBranch);
        Assert.Equal(2, request.Labels.Count);
    }

    [Fact]
    public void IssueAndPullRequestDtos_ShouldRemainSimpleAndSerializable()
    {
        var issue = new IssueDetails
        {
            Issue = new GitHubIssueReference("kcsnap/ai-sdlc-platform", 42, "https://github.com/kcsnap/ai-sdlc-platform/issues/42"),
            Title = "Automate risk scoring",
            BodyMarkdown = "Implement deterministic evaluation.",
            State = "open",
            Labels = new[] { "risk" },
            AuthorLogin = "kcsnap",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var pullRequest = new PullRequestDetails
        {
            PullRequest = new GitHubPullRequestReference("kcsnap/ai-sdlc-platform", 12, "ai/002-risk-rules-engine", "https://github.com/kcsnap/ai-sdlc-platform/pull/12"),
            Title = "Add risk rules engine",
            BodyMarkdown = "Implements Phase 2.",
            State = "open",
            BaseBranch = "main",
            HeadBranch = "ai/002-risk-rules-engine",
            AuthorLogin = "kcsnap",
            Labels = new[] { "automation" },
            Draft = false,
            Mergeable = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        Assert.Equal(42, issue.Issue.IssueNumber);
        Assert.Equal("ai/002-risk-rules-engine", pullRequest.PullRequest.BranchName);
        Assert.True(pullRequest.Mergeable);
    }

    [Fact]
    public async Task IGitHubService_ShouldExposeExpectedAsyncContract()
    {
        IGitHubService service = new FakeGitHubService();

        var issue = await service.GetIssueAsync("kcsnap/ai-sdlc-platform", 1, CancellationToken.None);
        var comments = await service.GetIssueCommentsAsync("kcsnap/ai-sdlc-platform", 1, CancellationToken.None);
        var checkRuns = await service.GetCheckRunResultsAsync("kcsnap/ai-sdlc-platform", "main", CancellationToken.None);

        Assert.Equal(1, issue.Issue.IssueNumber);
        Assert.Single(comments);
        Assert.Single(checkRuns);
    }

    private sealed class FakeGitHubService : IGitHubService
    {
        public Task<IReadOnlyList<string>> AddLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken) =>
            Task.FromResult(labels);

        public Task<IssueComment> AddIssueCommentAsync(string repository, int issueNumber, string markdown, CancellationToken cancellationToken) =>
            Task.FromResult(CreateComment(repository, issueNumber, markdown));

        public Task<IssueComment> AddPullRequestCommentAsync(string repository, int pullRequestNumber, string markdown, CancellationToken cancellationToken) =>
            Task.FromResult(CreateComment(repository, pullRequestNumber, markdown));

        public Task<GitHubPullRequestReference> CreatePullRequestAsync(CreatePullRequestRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GitHubPullRequestReference(request.Repository, 7, request.HeadBranch, $"https://github.com/{request.Repository}/pull/7"));

        public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChangedFile>>(new[]
            {
                new ChangedFile
                {
                    Path = "src/AiSdlc.GitHub/IGitHubService.cs",
                    Status = "modified",
                    Additions = 10,
                    Deletions = 1,
                    Changes = 11
                }
            });

        public Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string repository, string reference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CheckRunResult>>(new[]
            {
                new CheckRunResult
                {
                    Name = "dotnet-test",
                    Status = "completed",
                    Conclusion = "success",
                    IsRequired = true,
                    DetailsUrl = "https://github.com/kcsnap/ai-sdlc-platform/actions/runs/1"
                }
            });

        public Task<IssueDetails> GetIssueAsync(string repository, int issueNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new IssueDetails
            {
                Issue = new GitHubIssueReference(repository, issueNumber, $"https://github.com/{repository}/issues/{issueNumber}"),
                Title = "Issue title",
                BodyMarkdown = "Issue body",
                State = "open",
                Labels = new[] { "automation" },
                AuthorLogin = "kcsnap",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        public Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string repository, int issueNumber, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IssueComment>>(new[]
            {
                CreateComment(repository, issueNumber, "Looks good.")
            });

        public Task<PullRequestDetails> GetPullRequestAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new PullRequestDetails
            {
                PullRequest = new GitHubPullRequestReference(repository, pullRequestNumber, "feature/branch", $"https://github.com/{repository}/pull/{pullRequestNumber}"),
                Title = "PR title",
                BodyMarkdown = "PR body",
                State = "open",
                BaseBranch = "main",
                HeadBranch = "feature/branch",
                AuthorLogin = "kcsnap",
                Draft = false,
                Mergeable = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        public Task<IReadOnlyList<string>> RemoveLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        private static IssueComment CreateComment(string repository, int issueOrPullRequestNumber, string markdown) =>
            new()
            {
                CommentId = 1001,
                Repository = repository,
                IssueOrPullRequestNumber = issueOrPullRequestNumber,
                BodyMarkdown = markdown,
                AuthorLogin = "kcsnap",
                Url = $"https://github.com/{repository}/issues/{issueOrPullRequestNumber}#issuecomment-1001",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
    }
}
