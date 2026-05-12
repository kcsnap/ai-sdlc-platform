using AiSdlc.Agents;
using AiSdlc.GitHub;
using AiSdlc.RepoIndex;
using AiSdlc.Shared;
using AiSdlc.Shared.AutoMerge;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

public sealed class AgentActivityFunctions
{
    private readonly IAgentRunner _agentRunner;
    private readonly IGitHubService _gitHub;
    private readonly IRepoIndexer _repoIndexer;
    private readonly IAutoMergeEligibilityService _autoMergeEligibility;
    private readonly ILogger<AgentActivityFunctions> _logger;

    public AgentActivityFunctions(
        IAgentRunner agentRunner,
        IGitHubService gitHub,
        IRepoIndexer repoIndexer,
        IAutoMergeEligibilityService autoMergeEligibility,
        ILogger<AgentActivityFunctions> logger)
    {
        _agentRunner          = agentRunner;
        _gitHub               = gitHub;
        _repoIndexer          = repoIndexer;
        _autoMergeEligibility = autoMergeEligibility;
        _logger               = logger;
    }

    [Function(nameof(RunProductStrategistAsync))]
    public Task<AgentResult> RunProductStrategistAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ProductStrategist, context, cancellationToken);

    [Function(nameof(RunProductOwnerAsync))]
    public Task<AgentResult> RunProductOwnerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ProductOwner, context, cancellationToken);

    [Function(nameof(RunBusinessAnalystAsync))]
    public Task<AgentResult> RunBusinessAnalystAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.BusinessAnalyst, context, cancellationToken);

    [Function(nameof(RunArchitectAsync))]
    public Task<AgentResult> RunArchitectAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.Architect, context, cancellationToken);

    [Function(nameof(RunUxAccessibilityReviewerAsync))]
    public Task<AgentResult> RunUxAccessibilityReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.UxAccessibilityReviewer, context, cancellationToken);

    [Function(nameof(RunContentSeoReviewerAsync))]
    public Task<AgentResult> RunContentSeoReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ContentSeoReviewer, context, cancellationToken);

    [Function(nameof(RunDataAnalyticsReviewerAsync))]
    public Task<AgentResult> RunDataAnalyticsReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.DataAnalyticsReviewer, context, cancellationToken);

    [Function(nameof(RunComplianceLegalReviewerAsync))]
    public Task<AgentResult> RunComplianceLegalReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ComplianceLegalReviewer, context, cancellationToken);

    [Function(nameof(RunSecurityPrivacyReviewerAsync))]
    public Task<AgentResult> RunSecurityPrivacyReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.SecurityPrivacyReviewer, context, cancellationToken);

    [Function(nameof(RunDevOpsPlatformEngineerAsync))]
    public Task<AgentResult> RunDevOpsPlatformEngineerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.DevOpsPlatformEngineer, context, cancellationToken);

    [Function(nameof(RunQaTestEngineerAsync))]
    public Task<AgentResult> RunQaTestEngineerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.QaTestEngineer, context, cancellationToken);

    [Function(nameof(RunSeniorCoderAsync))]
    public Task<AgentResult> RunSeniorCoderAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.SeniorCoder, context, cancellationToken);

    [Function(nameof(RunRiskAssessorAsync))]
    public Task<AgentResult> RunRiskAssessorAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.RiskAssessor, context, cancellationToken);

    [Function(nameof(RunReleaseManagerAsync))]
    public Task<AgentResult> RunReleaseManagerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ReleaseManager, context, cancellationToken);

    [Function(nameof(PostGitHubCommentAsync))]
    public async Task PostGitHubCommentAsync([ActivityTrigger] PostCommentInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Posting comment to {Repository}#{Issue}", input.Repository, input.IssueNumber);
        await _gitHub.AddIssueCommentAsync(input.Repository, input.IssueNumber, input.Markdown, cancellationToken);
    }

    [Function(nameof(FetchRepoIndexAsync))]
    public async Task<string?> FetchRepoIndexAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching repo index for {Repository}", repository);
        var index = await _repoIndexer.IndexAsync(repository, cancellationToken);
        if (index is null)
        {
            _logger.LogInformation("No .ai-sdlc.yml found in {Repository} — skipping repo index.", repository);
            return null;
        }
        return RepoIndexMarkdownRenderer.Render(index);
    }

    [Function(nameof(AddGitHubLabelAsync))]
    public async Task AddGitHubLabelAsync([ActivityTrigger] AddLabelInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding label '{Label}' to {Repository}#{Issue}", input.Label, input.Repository, input.IssueOrPrNumber);
        await _gitHub.AddLabelsAsync(input.Repository, input.IssueOrPrNumber, [input.Label], cancellationToken);
    }

    [Function(nameof(GetPullRequestContextAsync))]
    public async Task<PrMergeContext> GetPullRequestContextAsync([ActivityTrigger] GetPrContextInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching PR context for {Repository}#{Pr}", input.Repository, input.PullRequestNumber);

        var pr       = await _gitHub.GetPullRequestAsync(input.Repository, input.PullRequestNumber, cancellationToken);
        var files    = await _gitHub.GetChangedFilesAsync(input.Repository, input.PullRequestNumber, cancellationToken);
        var checks   = await _gitHub.GetCheckRunResultsAsync(input.Repository, input.HeadSha, cancellationToken);

        var allChecksPass = checks.Count > 0
            && checks.All(c => c.Status == "completed" && c.Conclusion == "success");

        var hasTestCoverage = checks.Any(c =>
            (c.Name.Contains("test", StringComparison.OrdinalIgnoreCase) ||
             c.Name.Contains("coverage", StringComparison.OrdinalIgnoreCase))
            && c.Conclusion == "success")
            || files.Any(f => f.Path.Contains("test", StringComparison.OrdinalIgnoreCase));

        return new PrMergeContext(
            PullRequestNumber: input.PullRequestNumber,
            HeadSha:           input.HeadSha,
            Mergeable:         pr.Mergeable,
            AllChecksPass:     allChecksPass,
            HasTestCoverage:   hasTestCoverage,
            ChangedFiles:      files);
    }

    [Function(nameof(EvaluateAutoMergeAsync))]
    public Task<AutoMergeEligibilityResult> EvaluateAutoMergeAsync([ActivityTrigger] EvaluateMergeInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Evaluating auto-merge gates for {Repository} run {RunId}", input.Repository, input.RunId);

        var result = _autoMergeEligibility.Evaluate(new AutoMergeEligibilityRequest
        {
            RunId                       = input.RunId,
            Repository                  = input.Repository,
            RiskLevel                   = input.RiskLevel,
            RiskDecision                = input.RiskDecision,
            BriefApproved               = input.BriefApproved,
            AllReviewsCompleted         = input.AllReviewsCompleted,
            NoBlockingIssues            = input.NoBlockingIssues,
            AllChecksPass               = input.AllChecksPass,
            HasTestCoverage             = input.HasTestCoverage,
            RollbackDocumented          = input.RollbackDocumented,
            ReleaseNotesGenerated       = input.ReleaseNotesGenerated,
            PostDeploymentChecksDefinied = input.PostDeploymentChecksDefined
        });

        return Task.FromResult(result);
    }

    [Function(nameof(MergePullRequestActivityAsync))]
    public async Task MergePullRequestActivityAsync([ActivityTrigger] MergePrInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Merging PR #{Pr} on {Repository}", input.PullRequestNumber, input.Repository);
        await _gitHub.MergePullRequestAsync(input.Repository, input.PullRequestNumber, input.CommitMessage, cancellationToken);
    }

    private async Task<AgentResult> ExecuteAsync(string agentName, AgentContext context, CancellationToken cancellationToken)
    {
        var executionResult = await _agentRunner.ExecuteAsync(
            new AgentExecutionRequest { AgentName = agentName, Context = context },
            cancellationToken);

        if (!executionResult.Succeeded || executionResult.Result is null)
            throw new InvalidOperationException(executionResult.ErrorMessage ?? $"Agent execution failed for '{agentName}'.");

        return executionResult.Result;
    }
}
