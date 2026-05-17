using System.Text;
using AiSdlc.Agents;
using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.RepoIndex;
using AiSdlc.Shared;
using AiSdlc.Shared.AutoMerge;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

public sealed class AgentActivityFunctions
{
    // Truncation guards keep individual audit-event properties under Azure Table Storage's
    // 64 KB-per-property cap (chosen well below to leave headroom for UTF-16 expansion).
    private const int MaxSummaryLength    = 256;
    private const int MaxStackTraceLength = 30_000;

    private readonly IAgentRunner _agentRunner;
    private readonly IGitHubService _gitHub;
    private readonly IRepoIndexer _repoIndexer;
    private readonly IAutoMergeEligibilityService _autoMergeEligibility;
    private readonly IContextStore _contextStore;
    private readonly IAuditService _audit;
    private readonly ILogger<AgentActivityFunctions> _logger;

    public AgentActivityFunctions(
        IAgentRunner agentRunner,
        IGitHubService gitHub,
        IRepoIndexer repoIndexer,
        IAutoMergeEligibilityService autoMergeEligibility,
        IContextStore contextStore,
        IAuditService audit,
        ILogger<AgentActivityFunctions> logger)
    {
        _agentRunner          = agentRunner;
        _gitHub               = gitHub;
        _repoIndexer          = repoIndexer;
        _autoMergeEligibility = autoMergeEligibility;
        _contextStore         = contextStore;
        _audit                = audit;
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

        var markdown = input.Markdown;
        if (input.ContentRefs is { Count: > 0 })
        {
            var resolved = await Task.WhenAll(input.ContentRefs.Select(async kv =>
                (Sentinel: kv.Key, Content: await _contextStore.ResolveAsync(kv.Value, cancellationToken))));
            foreach (var (sentinel, content) in resolved)
                markdown = markdown.Replace(sentinel, content, StringComparison.Ordinal);
        }

        await _gitHub.AddIssueCommentAsync(input.Repository, input.IssueNumber, markdown, cancellationToken);
    }

    [Function(nameof(ResolveContextAsync))]
    public Task<string> ResolveContextAsync([ActivityTrigger] string contextRef, CancellationToken cancellationToken)
        => _contextStore.ResolveAsync(contextRef, cancellationToken);

    [Function(nameof(FetchRepoIndexAsync))]
    public async Task<AiSdlc.RepoIndex.RepoIndex?> FetchRepoIndexAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching repo index for {Repository}", repository);
        var index = await _repoIndexer.IndexAsync(repository, cancellationToken);
        if (index is null)
            _logger.LogInformation("No .ai-sdlc.yml found in {Repository} — skipping repo index.", repository);
        return index;
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

        var prTask     = _gitHub.GetPullRequestAsync(input.Repository, input.PullRequestNumber, cancellationToken);
        var filesTask  = _gitHub.GetChangedFilesAsync(input.Repository, input.PullRequestNumber, cancellationToken);
        var checksTask = _gitHub.GetCheckRunResultsAsync(input.Repository, input.HeadSha, cancellationToken);

        await Task.WhenAll(prTask, filesTask, checksTask);

        var pr     = await prTask;
        var files  = await filesTask;
        var checks = await checksTask;

        var allChecksPass = checks.Count == 0
            || checks.All(c => c.Status == "completed" && c.Conclusion == "success");

        var isDocsOnly = files.Count > 0
            && files.All(f => f.Path.EndsWith(".md",  StringComparison.OrdinalIgnoreCase)
                           || f.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                           || f.Path.EndsWith(".rst", StringComparison.OrdinalIgnoreCase));

        var hasTestCoverage = isDocsOnly
            || checks.Any(c =>
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
            PostDeploymentChecksDefined  = input.PostDeploymentChecksDefined
        });

        return Task.FromResult(result);
    }

    [Function(nameof(MergePullRequestActivityAsync))]
    public async Task MergePullRequestActivityAsync([ActivityTrigger] MergePrInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Merging PR #{Pr} on {Repository}", input.PullRequestNumber, input.Repository);
        await _gitHub.MergePullRequestAsync(input.Repository, input.PullRequestNumber, input.CommitMessage, cancellationToken);
    }

    [Function(nameof(RunCodeImplementerAsync))]
    public Task<AgentResult> RunCodeImplementerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.CodeImplementer, context, cancellationToken);

    [Function(nameof(GetDefaultBranchNameActivityAsync))]
    public async Task<string> GetDefaultBranchNameActivityAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting default branch name for {Repository}", repository);
        return await _gitHub.GetDefaultBranchAsync(repository, cancellationToken);
    }

    [Function(nameof(GetDefaultBranchShaActivityAsync))]
    public async Task<string> GetDefaultBranchShaActivityAsync([ActivityTrigger] GetHeadShaInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting HEAD SHA of {Branch} on {Repository}", input.Branch, input.Repository);
        return await _gitHub.GetDefaultBranchShaAsync(input.Repository, input.Branch, cancellationToken);
    }

    [Function(nameof(CreateBranchActivityAsync))]
    public async Task CreateBranchActivityAsync([ActivityTrigger] CreateBranchInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating branch {Branch} on {Repository} from {Sha}", input.BranchName, input.Repository, input.Sha);
        await _gitHub.CreateBranchAsync(input.Repository, input.BranchName, input.Sha, cancellationToken);
    }

    [Function(nameof(CommitFileAsync))]
    public async Task CommitFileAsync([ActivityTrigger] CommitFileInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Committing {Path} to {Branch} on {Repository}", input.Path, input.Branch, input.Repository);
        await _gitHub.CreateOrUpdateFileAsync(input.Repository, input.Path, input.Content, input.CommitMessage, input.Branch, cancellationToken);
    }

    [Function(nameof(CreatePrActivityAsync))]
    public async Task<GitHubPullRequestReference> CreatePrActivityAsync([ActivityTrigger] CreatePrActivityInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating PR '{Title}' on {Repository} from branch {Branch}", input.Title, input.Repository, input.BranchName);
        return await _gitHub.CreatePullRequestAsync(
            new CreatePullRequestRequest
            {
                Repository   = input.Repository,
                Title        = input.Title,
                BodyMarkdown = input.Body,
                HeadBranch   = input.BranchName,
                BaseBranch   = input.BaseBranch
            },
            cancellationToken);
    }

    [Function(nameof(ReviewBranchContentAsync))]
    public async Task<AgentResult> ReviewBranchContentAsync([ActivityTrigger] ReviewBranchInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Product Owner reviewing {FileCount} file(s) on branch {Branch} in {Repository}",
            input.FilePaths.Count, input.BranchName, input.Repository);

        var fetchTasks = input.FilePaths
            .Select(async path => (path, content: await _gitHub.GetBranchFileContentAsync(input.Repository, path, input.BranchName, cancellationToken)))
            .ToArray();
        var fetched = await Task.WhenAll(fetchTasks);

        var sb = new StringBuilder();
        foreach (var (path, content) in fetched)
        {
            if (content is not null)
            {
                sb.AppendLine($"### {path}");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        var context = new AgentContext
        {
            RunId          = input.RunId,
            Repository     = input.Repository,
            IssueNumber    = input.IssueNumber,
            CurrentState   = "Reviewing",
            RequestedAgent = AgentNames.ProductOwnerBranchReview,
            Metadata       =
            {
                ["branchContent"] = sb.ToString(),
                ["branchName"]    = input.BranchName,
                ["ownerBrief"]    = input.OwnerBrief,
                ["analystOutput"] = input.AnalystOutput
            }
        };

        return await ExecuteAsync(AgentNames.ProductOwnerBranchReview, context, cancellationToken);
    }

    private async Task<AgentResult> ExecuteAsync(string agentName, AgentContext context, CancellationToken cancellationToken)
    {
        await WriteAgentAuditAsync(agentName, context, action: "Started", summary: $"{agentName} started", cancellationToken: cancellationToken);

        AgentResult result;
        try
        {
            // Resolve blob references so the agent receives full content.
            // Metadata values are deserialized as JsonElement (not string) after Durable checkpointing,
            // so we must extract the string value from both types.
            foreach (var key in context.Metadata.Keys.ToList())
            {
                var strValue = context.Metadata[key] switch
                {
                    string s => s,
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
                        => je.GetString(),
                    _ => null
                };
                if (strValue is not null && _contextStore.IsReference(strValue))
                    context.Metadata[key] = await _contextStore.ResolveAsync(strValue, cancellationToken);
            }

            var executionResult = await _agentRunner.ExecuteAsync(
                new AgentExecutionRequest { AgentName = agentName, Context = context },
                cancellationToken);

            if (!executionResult.Succeeded || executionResult.Result is null)
                throw new InvalidOperationException(executionResult.ErrorMessage ?? $"Agent execution failed for '{agentName}'.");

            result = executionResult.Result;

            // Offload outputs larger than 1 KB to blob; null OutputMarkdown so Durable history stays slim
            if (result.OutputMarkdown?.Length > 1024)
            {
                var reference = await _contextStore.OffloadAsync(context.RunId, agentName, result.OutputMarkdown, cancellationToken);
                result = new AgentResult
                {
                    AgentName         = result.AgentName,
                    Status            = result.Status,
                    Summary           = result.Summary,
                    OutputMarkdown    = null,
                    ContextRef        = reference,
                    Decision          = result.Decision,
                    RiskLevel         = result.RiskLevel,
                    ArtefactsCreated  = result.ArtefactsCreated,
                    FollowUpQuestions = result.FollowUpQuestions,
                    BlockingIssues    = result.BlockingIssues
                };
            }
        }
        catch (Exception ex)
        {
            await WriteAgentAuditAsync(
                agentName,
                context,
                action: "Failed",
                summary: Truncate(ex.Message, MaxSummaryLength),
                references: new Dictionary<string, string>
                {
                    ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["stackTrace"]    = Truncate(ex.ToString(), MaxStackTraceLength)
                },
                cancellationToken: CancellationToken.None);  // audit must still write even if the activity was cancelled
            throw;
        }

        await WriteAgentAuditAsync(
            agentName,
            context,
            action: "Completed",
            summary: result.Summary,
            decision: result.Decision,
            riskLevel: result.RiskLevel,
            cancellationToken: cancellationToken);

        return result;
    }

    private async Task WriteAgentAuditAsync(
        string agentName,
        AgentContext context,
        string action,
        string summary,
        string? decision = null,
        string? riskLevel = null,
        Dictionary<string, string>? references = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = context.RunId,
                Repository  = context.Repository,
                IssueNumber = context.IssueNumber,
                PullRequestNumber = context.PullRequestNumber,
                ActorType   = "Agent",
                ActorName   = agentName,
                Action      = action,
                Summary     = summary,
                Decision    = decision,
                RiskLevel   = riskLevel,
                References  = references ?? new Dictionary<string, string>()
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit writes must never break agent execution — match the webhook handler's behaviour.
            _logger.LogWarning(ex, "Failed to write agent audit event {Action} for {Agent}.", action, agentName);
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}

public sealed record GetHeadShaInput(string Repository, string Branch);
public sealed record CreateBranchInput(string Repository, string BranchName, string Sha);
public sealed record CommitFileInput(string Repository, string Path, string Content,
                                     string CommitMessage, string Branch);
public sealed record CreatePrActivityInput(string Repository, string Title,
                                           string Body, string BranchName, string BaseBranch);
public sealed record ReviewBranchInput(
    string RunId,
    string Repository,
    int IssueNumber,
    string BranchName,
    IReadOnlyList<string> FilePaths,
    string OwnerBrief,
    string AnalystOutput);
