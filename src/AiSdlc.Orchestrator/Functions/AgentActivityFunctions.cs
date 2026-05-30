using System.Text;
using AiSdlc.Agents;
using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.RepoIndex;
using AiSdlc.RepoIndex.Charter;
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
    private readonly ICharterReader _charterReader;
    private readonly IAutoMergeEligibilityService _autoMergeEligibility;
    private readonly IContextStore _contextStore;
    private readonly IAuditService _audit;
    private readonly IBlobPromptStore _promptStore;
    private readonly ILogger<AgentActivityFunctions> _logger;

    public AgentActivityFunctions(
        IAgentRunner agentRunner,
        IGitHubService gitHub,
        IRepoIndexer repoIndexer,
        ICharterReader charterReader,
        IAutoMergeEligibilityService autoMergeEligibility,
        IContextStore contextStore,
        IAuditService audit,
        IBlobPromptStore promptStore,
        ILogger<AgentActivityFunctions> logger)
    {
        _agentRunner          = agentRunner;
        _gitHub               = gitHub;
        _repoIndexer          = repoIndexer;
        _charterReader        = charterReader;
        _autoMergeEligibility = autoMergeEligibility;
        _contextStore         = contextStore;
        _audit                = audit;
        _promptStore          = promptStore;
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

        var posted = await _gitHub.AddIssueCommentAsync(input.Repository, input.IssueNumber, markdown, cancellationToken);

        // Surface the comment URL in audit so the dashboard can link directly to it from the live feed.
        var summary = ExtractCommentHeading(markdown) ?? $"Comment posted on issue #{input.IssueNumber}";
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = BuildAuditRunId(input.Repository, input.IssueNumber),
                Repository  = input.Repository,
                IssueNumber = input.IssueNumber,
                ActorType   = "Comment",
                ActorName   = "GitHubComment",
                Action      = "Posted",
                Summary     = summary,
                References  = new Dictionary<string, string>
                {
                    ["commentUrl"] = posted.Url,
                    ["commentId"]  = posted.CommentId.ToString()
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write Comment audit event for {Repository}#{Issue}.", input.Repository, input.IssueNumber);
        }
    }

    private static string BuildAuditRunId(string repository, int issueNumber) =>
        $"{repository.Replace('/', '_')}_{issueNumber}";

    // The orchestrator builds markdown like "## AI SDLC — Specialist Reviews\n…" — return that
    // heading so the audit row reads naturally in the live feed.
    private static string? ExtractCommentHeading(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var firstLine = markdown.AsSpan().Trim();
        var newline = firstLine.IndexOf('\n');
        if (newline >= 0) firstLine = firstLine[..newline];
        var trimmed = firstLine.TrimStart('#').Trim();
        return trimmed.Length == 0 ? null : trimmed.ToString();
    }

    [Function(nameof(ResolveContextAsync))]
    public Task<string> ResolveContextAsync([ActivityTrigger] string contextRef, CancellationToken cancellationToken)
        => _contextStore.ResolveAsync(contextRef, cancellationToken);

    [Function(nameof(RecordWorkflowExitAsync))]
    public async Task RecordWorkflowExitAsync(
        [ActivityTrigger] WorkflowExitAuditInput input, CancellationToken cancellationToken)
    {
        // Writes a single Workflow-actor audit event when the orchestrator exits early as Stopped or Failed.
        // The dashboard reads this to flip the run's status chip and mark downstream agents as Skipped.
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = BuildAuditRunId(input.Repository, input.IssueNumber),
                Repository  = input.Repository,
                IssueNumber = input.IssueNumber,
                ActorType   = "Workflow",
                ActorName   = "Orchestrator",
                Action      = input.Outcome,
                Summary     = input.Reason,
                Decision    = input.Outcome
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write workflow-exit audit event for {Repo}#{Issue}.", input.Repository, input.IssueNumber);
        }
    }

    [Function(nameof(FetchRepoIndexAsync))]
    public async Task<AiSdlc.RepoIndex.RepoIndex?> FetchRepoIndexAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching repo index for {Repository}", repository);
        var index = await _repoIndexer.IndexAsync(repository, cancellationToken);
        if (index is null)
            _logger.LogInformation("No .ai-sdlc.yml found in {Repository} — skipping repo index.", repository);
        return index;
    }

    [Function(nameof(FetchCharterAsync))]
    public async Task<Charter?> FetchCharterAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching .yorrixx/charter.json for {Repository}", repository);
        var charter = await _charterReader.ReadAsync(repository, cancellationToken);
        if (charter is null)
            _logger.LogInformation("No usable charter found in {Repository} — skipping charter.", repository);
        return charter;
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
                ["analystOutput"] = input.AnalystOutput,
                ["charter"]       = input.Charter
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

            // Persist what the agent saw (input) and what it produced (output) to the prompts blob
            // so the dashboard's drill-down can render them. Done BEFORE the context-store offload
            // below so we capture the raw OutputMarkdown — the offload nulls it.
            await StoreAgentArtefactAsync(agentName, context, result, cancellationToken);

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

    // Writes the agent's input (serialised metadata) + raw output to blob storage so the dashboard
    // can show them on the drill-down. Failures are logged but never fail the agent execution.
    private async Task StoreAgentArtefactAsync(
        string agentName, AgentContext context, AgentResult result, CancellationToken cancellationToken)
    {
        try
        {
            var input  = await SerialiseAgentInputAsync(context, cancellationToken);
            var output = result.OutputMarkdown ?? string.Empty;
            await _promptStore.StoreAsync(context.RunId, agentName, input, output, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to store agent artefact for {Agent} on run {RunId}.",
                agentName, context.RunId);
        }
    }

    // Renders AgentContext.Metadata as readable markdown — a heading per key, fenced code block for
    // multi-line values. Resolves context-store references (ctx:...) so the dashboard sees actual
    // content, not opaque blob references.
    private async Task<string> SerialiseAgentInputAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append("# Agent input — ").AppendLine(context.RequestedAgent);
        sb.AppendLine();
        sb.Append("- **Run:** ").AppendLine(context.RunId);
        sb.Append("- **Repository:** ").AppendLine(context.Repository);
        sb.Append("- **Issue:** #").Append(context.IssueNumber).AppendLine();
        if (context.PullRequestNumber is int pr)
        {
            sb.Append("- **PR:** #").Append(pr).AppendLine();
        }
        sb.Append("- **State:** ").AppendLine(context.CurrentState);
        sb.AppendLine();

        if (context.Metadata.Count == 0)
        {
            sb.AppendLine("_No metadata supplied._");
            return sb.ToString();
        }

        sb.AppendLine("## Metadata");
        foreach (var (key, rawValue) in context.Metadata.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var value = rawValue?.ToString();
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Resolve context-store references so the dashboard sees the actual offloaded text.
            if (_contextStore.IsReference(value))
            {
                try { value = await _contextStore.ResolveAsync(value, cancellationToken); }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not resolve context ref for {Key}", key); }
            }

            sb.AppendLine();
            sb.Append("### ").AppendLine(key);
            if (value!.Contains('\n'))
            {
                sb.AppendLine("```");
                sb.AppendLine(value);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine(value);
            }
        }

        return sb.ToString();
    }
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
    string AnalystOutput,
    string Charter = "");
