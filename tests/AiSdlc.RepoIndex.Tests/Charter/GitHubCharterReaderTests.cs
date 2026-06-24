using AiSdlc.GitHub;
using AiSdlc.RepoIndex.Charter;
using AiSdlc.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiSdlc.RepoIndex.Tests.Charter;

public sealed class GitHubCharterReaderTests
{
    [Fact]
    public async Task Returns_null_when_file_missing()
    {
        var gh = new FakeGitHub(charterJson: null);
        var reader = new GitHubCharterReader(gh, NullLogger<GitHubCharterReader>.Instance);

        var charter = await reader.ReadAsync("yorrixx-apps/user-app-abc12345", CancellationToken.None);

        Assert.Null(charter);
    }

    [Fact]
    public async Task Returns_null_when_file_empty()
    {
        var gh = new FakeGitHub(charterJson: "   ");
        var reader = new GitHubCharterReader(gh, NullLogger<GitHubCharterReader>.Instance);

        var charter = await reader.ReadAsync("yorrixx-apps/user-app-abc12345", CancellationToken.None);

        Assert.Null(charter);
    }

    [Fact]
    public async Task Returns_null_when_json_malformed()
    {
        var gh = new FakeGitHub(charterJson: "{ not valid json");
        var reader = new GitHubCharterReader(gh, NullLogger<GitHubCharterReader>.Instance);

        var charter = await reader.ReadAsync("yorrixx-apps/user-app-abc12345", CancellationToken.None);

        Assert.Null(charter);
    }

    [Fact]
    public async Task Returns_null_when_schema_version_unrecognised()
    {
        var gh = new FakeGitHub(charterJson: """{ "SchemaVersion": 99, "Identity": { "AppName": "X" } }""");
        var reader = new GitHubCharterReader(gh, NullLogger<GitHubCharterReader>.Instance);

        var charter = await reader.ReadAsync("yorrixx-apps/user-app-abc12345", CancellationToken.None);

        Assert.Null(charter);
    }

    [Fact]
    public async Task Returns_parsed_charter_for_valid_payload()
    {
        var gh = new FakeGitHub(charterJson: """
            {
              "SchemaVersion": 1,
              "Identity": { "AppName": "TaskFlow", "OneLineDescription": "Solo tasks" },
              "Audience": { "PrimaryUserDescription": "Solo devs", "ExpectedScale": "Solo" },
              "Constraints": { "DataSensitivity": "Low", "NeedsAuth": true }
            }
            """);
        var reader = new GitHubCharterReader(gh, NullLogger<GitHubCharterReader>.Instance);

        var charter = await reader.ReadAsync("yorrixx-apps/user-app-abc12345", CancellationToken.None);

        Assert.NotNull(charter);
        Assert.Equal("TaskFlow", charter!.Identity.AppName);
        Assert.Equal(ExpectedScale.Solo, charter.Audience.ExpectedScale);
    }

    [Fact]
    public async Task Reads_canonical_path()
    {
        var gh = new FakeGitHub(charterJson: """{ "SchemaVersion": 1 }""");
        var reader = new GitHubCharterReader(gh, NullLogger<GitHubCharterReader>.Instance);

        await reader.ReadAsync("yorrixx-apps/user-app-abc12345", CancellationToken.None);

        Assert.Equal(GitHubCharterReader.CharterPath, gh.LastRequestedPath);
        Assert.Equal(".yorrixx/charter.json", gh.LastRequestedPath);
    }

    private sealed class FakeGitHub : IGitHubService
    {
        private readonly string? _charterJson;
        public string? LastRequestedPath { get; private set; }

        public FakeGitHub(string? charterJson) { _charterJson = charterJson; }

        public Task<string?> GetFileContentAsync(string repository, string path, CancellationToken cancellationToken)
        {
            LastRequestedPath = path;
            return Task.FromResult(_charterJson);
        }

        // Unused — these tests only exercise GetFileContentAsync.
        public Task<IssueDetails> GetIssueAsync(string r, int i, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string r, int i, CancellationToken ct) => throw new NotImplementedException();
        public Task<IssueComment> AddIssueCommentAsync(string r, int i, string m, CancellationToken ct) => throw new NotImplementedException();
        public Task<IssueComment> AddPullRequestCommentAsync(string r, int p, string m, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> AddLabelsAsync(string r, int n, IReadOnlyList<string> l, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> RemoveLabelsAsync(string r, int n, IReadOnlyList<string> l, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubPullRequestReference> CreatePullRequestAsync(CreatePullRequestRequest r, CancellationToken ct) => throw new NotImplementedException();
        public Task<PullRequestDetails> GetPullRequestAsync(string r, int p, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string r, int p, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string r, string s, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetBranchFileContentAsync(string r, string p, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<RepoTreeEntry>> GetBranchFileTreeAsync(string repository, string branch, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RepoTreeEntry>>([]);
        public Task<IReadOnlyList<FailedCheckFinding>> GetFailedCheckFindingsAsync(string repository, string reference, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FailedCheckFinding>>([]);
        public Task<OpenPullRequestInfo?> GetNewestOpenPullRequestByBranchPrefixAsync(string repository, string branchPrefix, CancellationToken cancellationToken) => Task.FromResult<OpenPullRequestInfo?>(null);
        public Task MergePullRequestAsync(string r, int p, string m, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> GetDefaultBranchAsync(string r, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> GetDefaultBranchShaAsync(string r, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateBranchAsync(string r, string b, string s, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateOrUpdateFileAsync(string r, string p, string c, string m, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrgIssueSearchHit>> SearchOpenOrgIssuesByLabelAsync(string o, string l, CancellationToken ct) => throw new NotImplementedException();
        public Task<CreatedRepository> CreateRepositoryFromTemplateAsync(string t, string o, string n, bool p, string d, CancellationToken ct) => throw new NotImplementedException();
        public Task SetRepoVariableAsync(string r, string n, string v, CancellationToken ct) => throw new NotImplementedException();
    }
}
