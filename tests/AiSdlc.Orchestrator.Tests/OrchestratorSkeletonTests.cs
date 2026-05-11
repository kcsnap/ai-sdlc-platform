using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.GitHub;
using AiSdlc.ModelProviders;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class OrchestratorSkeletonTests
{
    private static AgentActivityFunctions BuildActivityFunctions()
    {
        var fakeModel = new FakeModelProvider(new ModelProviderOptions
        {
            ProviderName     = "Fake",
            ModelName        = "fake-model",
            DefaultMaxTokens = 1024
        });

        return new(
            new AgentRunner([
                new ProductStrategistAgent(fakeModel),
                new ProductOwnerAgent(fakeModel),
                new BusinessAnalystAgent(fakeModel)
            ]),
            new NoOpGitHubService(),
            NullLogger<AgentActivityFunctions>.Instance);
    }

    [Fact]
    public async Task AgentActivityFunctions_ShouldExecuteRegisteredPersonaActivities()
    {
        var functions = BuildActivityFunctions();
        var context = new AgentContext
        {
            RunId          = "run-123",
            Repository     = "kcsnap/ai-sdlc-platform",
            IssueNumber    = 42,
            CurrentState   = WorkflowRunStatus.Started.ToString(),
            RequestedAgent = AgentNames.ProductStrategist
        };

        var strategistResult = await functions.RunProductStrategistAsync(context, CancellationToken.None);
        var ownerResult      = await functions.RunProductOwnerAsync(context, CancellationToken.None);
        var analystResult    = await functions.RunBusinessAnalystAsync(context, CancellationToken.None);

        Assert.Equal(AgentNames.ProductStrategist, strategistResult.AgentName);
        Assert.Equal(AgentNames.ProductOwner,      ownerResult.AgentName);
        Assert.Equal(AgentNames.BusinessAnalyst,   analystResult.AgentName);
    }

    [Fact]
    public async Task PostGitHubCommentAsync_CallsGitHubService()
    {
        var functions = BuildActivityFunctions();
        var input = new PostCommentInput("kcsnap/ai-sdlc-platform", 42, "## Brief\n\nSome content.");

        // NoOpGitHubService swallows the call — we just assert it doesn't throw
        await functions.PostGitHubCommentAsync(input, CancellationToken.None);
    }

    [Fact]
    public async Task AddGitHubLabelAsync_CallsGitHubService()
    {
        var functions = BuildActivityFunctions();
        var input = new AddLabelInput("kcsnap/ai-sdlc-platform", 42, "ai-sdlc:awaiting-brief-approval");

        await functions.AddGitHubLabelAsync(input, CancellationToken.None);
    }

    [Fact]
    public void FunctionTypes_ShouldExposeExpectedSkeletonClasses()
    {
        Assert.NotNull(typeof(AiSdlcWorkflowOrchestrator));
        Assert.NotNull(typeof(GitHubWebhookFunction));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunProductStrategistAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunProductOwnerAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunBusinessAnalystAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.PostGitHubCommentAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.AddGitHubLabelAsync)));
    }

    // Minimal no-op IGitHubService — returns empty/stub values; swallows writes.
    private sealed class NoOpGitHubService : IGitHubService
    {
        public Task<IssueDetails> GetIssueAsync(string repository, int issueNumber, CancellationToken ct) =>
            Task.FromResult(new IssueDetails
            {
                Issue        = new GitHubIssueReference(repository, issueNumber, $"https://github.com/{repository}/issues/{issueNumber}"),
                Title        = "Test issue",
                BodyMarkdown = string.Empty,
                State        = "open",
                AuthorLogin  = "test"
            });

        public Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string repository, int issueNumber, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IssueComment>>([]);

        public Task<IssueComment> AddIssueCommentAsync(string repository, int issueNumber, string markdown, CancellationToken ct) =>
            Task.FromResult(StubComment(repository, issueNumber));

        public Task<IssueComment> AddPullRequestCommentAsync(string repository, int prNumber, string markdown, CancellationToken ct) =>
            Task.FromResult(StubComment(repository, prNumber));

        public Task<IReadOnlyList<string>> AddLabelsAsync(string repository, int number, IReadOnlyList<string> labels, CancellationToken ct) =>
            Task.FromResult(labels);

        public Task<IReadOnlyList<string>> RemoveLabelsAsync(string repository, int number, IReadOnlyList<string> labels, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<GitHubPullRequestReference> CreatePullRequestAsync(CreatePullRequestRequest request, CancellationToken ct) =>
            Task.FromResult(new GitHubPullRequestReference(request.Repository, 1, request.HeadBranch, $"https://github.com/{request.Repository}/pull/1"));

        public Task<PullRequestDetails> GetPullRequestAsync(string repository, int prNumber, CancellationToken ct) =>
            Task.FromResult(new PullRequestDetails
            {
                PullRequest  = new GitHubPullRequestReference(repository, prNumber, "feature", $"https://github.com/{repository}/pull/{prNumber}"),
                Title        = "Test PR",
                BodyMarkdown = string.Empty,
                State        = "open",
                BaseBranch   = "main",
                HeadBranch   = "feature",
                AuthorLogin  = "test"
            });

        public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int prNumber, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChangedFile>>([]);

        public Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string repository, string reference, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CheckRunResult>>([]);

        private static IssueComment StubComment(string repository, int number) => new()
        {
            CommentId                = 1,
            Repository               = repository,
            IssueOrPullRequestNumber = number,
            BodyMarkdown             = string.Empty,
            AuthorLogin              = "ai-sdlc[bot]",
            Url                      = $"https://github.com/{repository}/issues/{number}#issuecomment-1",
            CreatedAtUtc             = DateTimeOffset.UtcNow
        };
    }
}
