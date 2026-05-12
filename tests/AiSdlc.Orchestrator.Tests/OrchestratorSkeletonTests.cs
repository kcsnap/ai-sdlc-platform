using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.GitHub;
using AiSdlc.ModelProviders;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using AiSdlc.Shared.AutoMerge;
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
                new BusinessAnalystAgent(fakeModel),
                new ArchitectAgent(fakeModel),
                new UxAccessibilityReviewerAgent(fakeModel),
                new ContentSeoReviewerAgent(fakeModel),
                new DataAnalyticsReviewerAgent(fakeModel),
                new ComplianceLegalReviewerAgent(fakeModel),
                new SecurityPrivacyReviewerAgent(fakeModel),
                new DevOpsPlatformEngineerAgent(fakeModel),
                new QaTestEngineerAgent(fakeModel),
                new SeniorCoderAgent(fakeModel),
                new RiskAssessorAgent(fakeModel),
                new ReleaseManagerAgent(fakeModel)
            ]),
            new NoOpGitHubService(),
            new NoOpRepoIndexer(),
            new AutoMergeEligibilityService(),
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
    public async Task NewPersonaActivities_Execute()
    {
        var functions = BuildActivityFunctions();
        var context = new AgentContext
        {
            RunId = "run-456", Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 42, CurrentState = WorkflowRunStatus.Analysing.ToString(),
            RequestedAgent = AgentNames.Architect
        };

        var archResult     = await functions.RunArchitectAsync(context, CancellationToken.None);
        var secResult      = await functions.RunSecurityPrivacyReviewerAsync(context, CancellationToken.None);
        var uxResult       = await functions.RunUxAccessibilityReviewerAsync(context, CancellationToken.None);
        var devopsResult   = await functions.RunDevOpsPlatformEngineerAsync(context, CancellationToken.None);
        var qaResult       = await functions.RunQaTestEngineerAsync(context, CancellationToken.None);
        var coderResult    = await functions.RunSeniorCoderAsync(context, CancellationToken.None);
        var riskResult     = await functions.RunRiskAssessorAsync(context, CancellationToken.None);
        var releaseResult  = await functions.RunReleaseManagerAsync(context, CancellationToken.None);

        Assert.Equal(AgentNames.Architect,              archResult.AgentName);
        Assert.Equal(AgentNames.SecurityPrivacyReviewer, secResult.AgentName);
        Assert.Equal(AgentNames.UxAccessibilityReviewer, uxResult.AgentName);
        Assert.Equal(AgentNames.DevOpsPlatformEngineer,  devopsResult.AgentName);
        Assert.Equal(AgentNames.QaTestEngineer,          qaResult.AgentName);
        Assert.Equal(AgentNames.SeniorCoder,             coderResult.AgentName);
        Assert.Equal(AgentNames.RiskAssessor,            riskResult.AgentName);
        Assert.Equal(AgentNames.ReleaseManager,          releaseResult.AgentName);
    }

    [Fact]
    public async Task GetPullRequestContextAsync_ReturnsPrMergeContext()
    {
        var functions = BuildActivityFunctions();
        var input = new GetPrContextInput("kcsnap/launchcart", 7, "abc123");

        var result = await functions.GetPullRequestContextAsync(input, CancellationToken.None);

        Assert.Equal(7, result.PullRequestNumber);
        Assert.Equal("abc123", result.HeadSha);
        // NoOpGitHubService returns empty check runs — AllChecksPass is false, HasTestCoverage is false
        Assert.False(result.AllChecksPass);
    }

    [Fact]
    public async Task EvaluateAutoMergeAsync_ReturnsResultWithFailedGates_WhenChecksNotPassed()
    {
        var functions = BuildActivityFunctions();
        var input = new EvaluateMergeInput(
            RunId: "run-1", Repository: "kcsnap/launchcart",
            RiskLevel: RiskLevel.Low, RiskDecision: "AUTO_MERGE_ELIGIBLE",
            BriefApproved: true, AllReviewsCompleted: true, NoBlockingIssues: true,
            AllChecksPass: false, HasTestCoverage: false,
            RollbackDocumented: true, ReleaseNotesGenerated: true, PostDeploymentChecksDefined: true);

        var result = await functions.EvaluateAutoMergeAsync(input, CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Contains(result.FailedGates, g => g.Contains("CI checks"));
    }

    [Fact]
    public async Task EvaluateAutoMergeAsync_ReturnsEligible_WhenAllGatesPass()
    {
        var functions = BuildActivityFunctions();
        var input = new EvaluateMergeInput(
            RunId: "run-2", Repository: "kcsnap/launchcart",
            RiskLevel: RiskLevel.Low, RiskDecision: "AUTO_MERGE_ELIGIBLE",
            BriefApproved: true, AllReviewsCompleted: true, NoBlockingIssues: true,
            AllChecksPass: true, HasTestCoverage: true,
            RollbackDocumented: true, ReleaseNotesGenerated: true, PostDeploymentChecksDefined: true);

        var result = await functions.EvaluateAutoMergeAsync(input, CancellationToken.None);

        Assert.True(result.IsEligible);
        Assert.Empty(result.FailedGates);
    }

    [Fact]
    public async Task MergePullRequestActivityAsync_DoesNotThrow()
    {
        var functions = BuildActivityFunctions();
        var input = new MergePrInput("kcsnap/launchcart", 7, "feat: my feature (closes #42)");

        await functions.MergePullRequestActivityAsync(input, CancellationToken.None);
    }

    [Fact]
    public void FunctionTypes_ShouldExposeExpectedSkeletonClasses()
    {
        Assert.NotNull(typeof(AiSdlcWorkflowOrchestrator));
        Assert.NotNull(typeof(GitHubWebhookFunction));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunProductStrategistAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunProductOwnerAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunBusinessAnalystAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunArchitectAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunRiskAssessorAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunReleaseManagerAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.PostGitHubCommentAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.AddGitHubLabelAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.GetPullRequestContextAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.EvaluateAutoMergeAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.MergePullRequestActivityAsync)));
    }

    private sealed class NoOpRepoIndexer : AiSdlc.RepoIndex.IRepoIndexer
    {
        public Task<AiSdlc.RepoIndex.RepoIndex?> IndexAsync(string repository, CancellationToken cancellationToken)
            => Task.FromResult<AiSdlc.RepoIndex.RepoIndex?>(null);
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

        public Task<string?> GetFileContentAsync(string repository, string path, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public Task MergePullRequestAsync(string repository, int pullRequestNumber, string commitMessage, CancellationToken ct) =>
            Task.CompletedTask;

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
