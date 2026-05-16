using System.Text;
using System.Text.RegularExpressions;
using AiSdlc.Agents;
using AiSdlc.GitHub;
using AiSdlc.GitHub.Webhooks;
using AiSdlc.RepoIndex;
using AiSdlc.Shared;
using AiSdlc.Shared.AutoMerge;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace AiSdlc.Orchestrator.Functions;

public static class AiSdlcWorkflowOrchestrator
{
    private const int MaxBriefAttempts = 3;
    private static readonly TimeSpan BriefApprovalTimeout  = TimeSpan.FromDays(7);
    private static readonly TimeSpan HumanReviewTimeout    = TimeSpan.FromDays(14);
    private static readonly TimeSpan PrReadyTimeout        = TimeSpan.FromDays(30);
    private static readonly TimeSpan MergeApprovalTimeout  = TimeSpan.FromDays(14);

    // Retry agent activities up to 3× (Durable-level) when Anthropic rate-limits us.
    // AnthropicModelProvider already does 2 HTTP retries (≈1 min); these Durable retries
    // add wider spacing so concurrent fan-out calls don't all hit the API together.
    private static readonly TaskOptions AgentRetryOptions = TaskOptions.FromRetryPolicy(
        new RetryPolicy(maxNumberOfAttempts: 3, firstRetryInterval: TimeSpan.FromMinutes(3), backoffCoefficient: 2.0));

    [Function(nameof(AiSdlcWorkflowOrchestrator))]
    public static async Task<WorkflowRun> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var agentContext = context.GetInput<AgentContext>()
            ?? throw new InvalidOperationException("Workflow input must include an AgentContext payload.");

        var issue     = BuildIssueRef(agentContext);
        var createdAt = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

        // ── Step 0: Fetch repo index ───────────────────────────────────────────
        var repoIndex = await context.CallActivityAsync<AiSdlc.RepoIndex.RepoIndex?>(
            nameof(AgentActivityFunctions.FetchRepoIndexAsync), agentContext.Repository);
        if (repoIndex is not null)
            agentContext.Metadata["repoContext"] = RepoIndexMarkdownRenderer.Render(repoIndex);
        var allowAutoMerge = repoIndex?.AllowLowRiskAutoMerge ?? false;

        // ── Step 1: Product Strategist ─────────────────────────────────────────
        var strategistResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunProductStrategistAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["strategistOutput"] = strategistResult.OutputMarkdown ?? strategistResult.Summary;

        // ── Step 2: Product Owner — brief, auto-approved when allowAutoMerge ────
        AgentResult ownerResult;
        bool briefApproved;

        if (allowAutoMerge)
        {
            ownerResult = await context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunProductOwnerAsync), agentContext, AgentRetryOptions);

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    BuildBriefComment(ownerResult, 0)));

            briefApproved = true;
        }
        else
        {
            ownerResult   = strategistResult;
            briefApproved = false;

            for (var attempt = 0; attempt < MaxBriefAttempts && !briefApproved; attempt++)
            {
                ownerResult = await context.CallActivityAsync<AgentResult>(
                    nameof(AgentActivityFunctions.RunProductOwnerAsync), agentContext);

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        BuildBriefComment(ownerResult, attempt)));

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                    new AddLabelInput(agentContext.Repository, agentContext.IssueNumber,
                        "ai-sdlc:awaiting-brief-approval"));

                using var cts = new CancellationTokenSource();
                var approveTask = context.WaitForExternalEvent<object?>(WorkflowEventNames.ApproveBrief,  cts.Token);
                var changesTask = context.WaitForExternalEvent<object?>(WorkflowEventNames.RequestChanges, cts.Token);
                var timeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(BriefApprovalTimeout), cts.Token);

                var winner = await Task.WhenAny(approveTask, changesTask, timeoutTask);
                cts.Cancel();

                if (winner == approveTask)           { briefApproved = true; break; }
                if (winner == timeoutTask)           return Stopped(agentContext.RunId, issue, createdAt, context);
                if (attempt == MaxBriefAttempts - 1) return Stopped(agentContext.RunId, issue, createdAt, context);
            }
        }

        agentContext.Metadata["ownerBrief"] = ownerResult.OutputMarkdown ?? ownerResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.AddGitHubLabelAsync),
            new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:brief-approved"));

        // ── Step 3: Business Analyst ───────────────────────────────────────────
        var analystResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunBusinessAnalystAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["analystOutput"] = analystResult.OutputMarkdown ?? analystResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                BuildSectionComment("AI SDLC — Business Analysis", analystResult)));

        // ── Step 4: Architect ──────────────────────────────────────────────────
        var architectResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunArchitectAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["architectOutput"] = architectResult.OutputMarkdown ?? architectResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                BuildSectionComment("AI SDLC — Architecture Review", architectResult)));

        // ── Step 5: Parallel specialist reviews (fan-out) ─────────────────────
        var reviewTasks = new List<Task<AgentResult>>
        {
            context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunSecurityPrivacyReviewerAsync), agentContext, AgentRetryOptions),
            context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunUxAccessibilityReviewerAsync), agentContext, AgentRetryOptions),
            context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunDevOpsPlatformEngineerAsync),  agentContext, AgentRetryOptions),
            context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunContentSeoReviewerAsync),      agentContext, AgentRetryOptions),
            context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunComplianceLegalReviewerAsync), agentContext, AgentRetryOptions),
            context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunDataAnalyticsReviewerAsync),   agentContext, AgentRetryOptions),
        };

        var reviewResults = await Task.WhenAll(reviewTasks);

        var securityResult   = reviewResults[0];
        var uxResult         = reviewResults[1];
        var devopsResult     = reviewResults[2];
        var contentResult    = reviewResults[3];
        var complianceResult = reviewResults[4];
        var analyticsResult  = reviewResults[5];

        agentContext.Metadata["securityOutput"]   = securityResult.OutputMarkdown   ?? securityResult.Summary;
        agentContext.Metadata["uxOutput"]         = uxResult.OutputMarkdown         ?? uxResult.Summary;
        agentContext.Metadata["devopsOutput"]     = devopsResult.OutputMarkdown     ?? devopsResult.Summary;
        agentContext.Metadata["contentOutput"]    = contentResult.OutputMarkdown    ?? contentResult.Summary;
        agentContext.Metadata["complianceOutput"] = complianceResult.OutputMarkdown ?? complianceResult.Summary;
        agentContext.Metadata["analyticsOutput"]  = analyticsResult.OutputMarkdown  ?? analyticsResult.Summary;
        agentContext.Metadata["specialistReviews"] = BuildSpecialistReviewsText(
            securityResult, uxResult, devopsResult, contentResult, complianceResult, analyticsResult);

        // Post all specialist reviews as a single consolidated comment
        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                BuildSpecialistReviewsComment(securityResult, uxResult, devopsResult, contentResult, complianceResult, analyticsResult)));

        // ── Step 6: QA + Senior Coder (parallel) ──────────────────────────────
        var qaTask     = context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunQaTestEngineerAsync), agentContext, AgentRetryOptions);
        var coderTask  = context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunSeniorCoderAsync),    agentContext, AgentRetryOptions);

        var qaResult    = await qaTask;
        var coderResult = await coderTask;

        agentContext.Metadata["testPlan"] = qaResult.OutputMarkdown    ?? qaResult.Summary;
        agentContext.Metadata["implSpec"] = coderResult.OutputMarkdown ?? coderResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                BuildImplementationComment(qaResult, coderResult)));

        // ── Step 7: Risk Assessor ──────────────────────────────────────────────
        var riskResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunRiskAssessorAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["riskAssessment"] = riskResult.OutputMarkdown ?? riskResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                BuildSectionComment("AI SDLC — Risk Assessment", riskResult)));

        // ── Step 8: Route on risk decision ────────────────────────────────────
        var riskDecision = riskResult.Decision ?? "HUMAN_REVIEW_REQUIRED";

        if (riskDecision == "BLOCKED")
            return Failed(agentContext.RunId, issue, createdAt, context, riskResult);

        if (riskDecision == "HUMAN_REVIEW_REQUIRED")
        {
            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:awaiting-human-review"));

            using var cts = new CancellationTokenSource();
            var approveTask = context.WaitForExternalEvent<object?>(WorkflowEventNames.ApproveRelease, cts.Token);
            var timeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(HumanReviewTimeout),  cts.Token);
            var winner      = await Task.WhenAny(approveTask, timeoutTask);
            cts.Cancel();

            if (winner == timeoutTask)
                return Stopped(agentContext.RunId, issue, createdAt, context);
        }

        // ── Step 9: Release Manager ────────────────────────────────────────────
        var releaseResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunReleaseManagerAsync), agentContext, AgentRetryOptions);

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                BuildDevReadyComment(releaseResult, riskDecision)));

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.AddGitHubLabelAsync),
            new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:analysis-ready"));

        // ── Phase 2: Code implementation → PR → evaluate gates → merge ─────────

        var issueTitle = agentContext.Metadata.TryGetValue("issueTitle", out var titleMeta)
            ? titleMeta?.ToString() ?? "change" : "change";

        GitHubPullRequestReference prRef    = null!;
        PrMergeContext             prContext = null!;

        if (allowAutoMerge)
        {
            // ── Step 10: Generate code implementation ─────────────────────────
            var implResult = await context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunCodeImplementerAsync), agentContext, AgentRetryOptions);

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    BuildSectionComment("AI SDLC — Implementation", implResult)));

            var fileChanges = CodeChangeParser.Parse(implResult.OutputMarkdown);

            // ── Step 11: Create branch and commit files ────────────────────────
            var slug          = GenerateBranchSlug(issueTitle);
            var branchName    = $"ai/{agentContext.IssueNumber}-{slug}";

            var defaultBranch = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.GetDefaultBranchNameActivityAsync),
                agentContext.Repository);

            var headSha = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                new GetHeadShaInput(agentContext.Repository, defaultBranch));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.CreateBranchActivityAsync),
                new CreateBranchInput(agentContext.Repository, branchName, headSha));

            var commitMsg = $"feat: {issueTitle} (closes #{agentContext.IssueNumber}) [ai-sdlc]";
            foreach (var file in fileChanges)
            {
                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.CommitFileAsync),
                    new CommitFileInput(agentContext.Repository, file.Path, file.Content, commitMsg, branchName));
            }

            // Get branch HEAD SHA after all commits
            var prHeadSha = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                new GetHeadShaInput(agentContext.Repository, branchName));

            // ── Step 11b: Product Owner reviews committed content ──────────────
            if (fileChanges.Count > 0)
            {
                var branchReviewResult = await context.CallActivityAsync<AgentResult>(
                    nameof(AgentActivityFunctions.ReviewBranchContentAsync),
                    new ReviewBranchInput(
                        agentContext.RunId, agentContext.Repository, agentContext.IssueNumber,
                        branchName, fileChanges.Select(f => f.Path).ToArray(),
                        agentContext.Metadata.GetValueOrDefault("ownerBrief")?.ToString()    ?? string.Empty,
                        agentContext.Metadata.GetValueOrDefault("analystOutput")?.ToString() ?? string.Empty),
                    AgentRetryOptions);

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        BuildSectionComment("AI SDLC — Implementation Review", branchReviewResult)));

                if (branchReviewResult.Decision == "CHANGES_REQUIRED")
                {
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                        new AddLabelInput(agentContext.Repository, agentContext.IssueNumber,
                            "ai-sdlc:implementation-review-failed"));
                    return Stopped(agentContext.RunId, issue, createdAt, context);
                }
            }

            // ── Step 12: Open PR ───────────────────────────────────────────────
            var prBody = $"Closes #{agentContext.IssueNumber}\n\n{implResult.Summary}";
            prRef = await context.CallActivityAsync<GitHubPullRequestReference>(
                nameof(AgentActivityFunctions.CreatePrActivityAsync),
                new CreatePrActivityInput(agentContext.Repository, issueTitle, prBody, branchName, defaultBranch));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:pr-opened"));

            prContext = await context.CallActivityAsync<PrMergeContext>(
                nameof(AgentActivityFunctions.GetPullRequestContextAsync),
                new GetPrContextInput(agentContext.Repository, prRef.PullRequestNumber, prHeadSha));
        }
        else
        {
            // ── Step 10: Wait for a human-created PR ──────────────────────────
            using var prCts   = new CancellationTokenSource();
            var prReadyTask   = context.WaitForExternalEvent<PrReadyPayload>(WorkflowEventNames.PullRequestReady, prCts.Token);
            var prTimeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(PrReadyTimeout), prCts.Token);
            var prWinner      = await Task.WhenAny(prReadyTask, prTimeoutTask);
            prCts.Cancel();

            if (prWinner == prTimeoutTask)
                return Stopped(agentContext.RunId, issue, createdAt, context);

            var prPayload = await prReadyTask;

            prRef = new GitHubPullRequestReference(
                agentContext.Repository, prPayload.PullRequestNumber, string.Empty,
                $"https://github.com/{agentContext.Repository}/pull/{prPayload.PullRequestNumber}");

            prContext = await context.CallActivityAsync<PrMergeContext>(
                nameof(AgentActivityFunctions.GetPullRequestContextAsync),
                new GetPrContextInput(agentContext.Repository, prRef.PullRequestNumber, prPayload.HeadSha));
        }

        // Step 12: Evaluate all 10 auto-merge gates
        var noBlockingIssues = reviewResults.All(r => r.BlockingIssues.Count == 0)
                               && qaResult.BlockingIssues.Count == 0
                               && coderResult.BlockingIssues.Count == 0;

        var rollbackDocumented  = releaseResult.OutputMarkdown?.Contains("rollback", StringComparison.OrdinalIgnoreCase) ?? false;
        var releaseNotesGenerated = !string.IsNullOrWhiteSpace(releaseResult.OutputMarkdown);
        var postDeployDefined   = releaseResult.OutputMarkdown?.Contains("post-deploy", StringComparison.OrdinalIgnoreCase) ?? false;

        var eligibility = await context.CallActivityAsync<AutoMergeEligibilityResult>(
            nameof(AgentActivityFunctions.EvaluateAutoMergeAsync),
            new EvaluateMergeInput(
                RunId:                   agentContext.RunId,
                Repository:              agentContext.Repository,
                RiskLevel:               ParseRiskLevel(riskDecision),
                RiskDecision:            riskDecision,
                BriefApproved:           briefApproved,
                AllReviewsCompleted:     true,
                NoBlockingIssues:        noBlockingIssues,
                AllChecksPass:           prContext.AllChecksPass,
                HasTestCoverage:         prContext.HasTestCoverage,
                RollbackDocumented:      rollbackDocumented,
                ReleaseNotesGenerated:   releaseNotesGenerated,
                PostDeploymentChecksDefined: postDeployDefined));

        var commitMessage = $"feat: {issueTitle} (closes #{agentContext.IssueNumber})";

        if (riskDecision == "AUTO_MERGE_ELIGIBLE" && eligibility.IsEligible && allowAutoMerge)
        {
            // All gates pass and repo has opted in — merge automatically
            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.MergePullRequestActivityAsync),
                new MergePrInput(agentContext.Repository, prRef.PullRequestNumber, commitMessage));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    BuildAutoMergedComment(prRef.PullRequestNumber, eligibility)));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:auto-merged"));
        }
        else
        {
            // Gate failure or medium risk — post results and await human approval
            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    BuildGateResultsComment(prRef.PullRequestNumber, eligibility, riskDecision, allowAutoMerge)));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:awaiting-human-review"));

            using var mergeCts      = new CancellationTokenSource();
            var approveTask         = context.WaitForExternalEvent<object?>(WorkflowEventNames.HumanReviewApproved, mergeCts.Token);
            var mergeTimeoutTask    = context.CreateTimer(context.CurrentUtcDateTime.Add(MergeApprovalTimeout), mergeCts.Token);
            var mergeWinner         = await Task.WhenAny(approveTask, mergeTimeoutTask);
            mergeCts.Cancel();

            if (mergeWinner == mergeTimeoutTask)
                return Stopped(agentContext.RunId, issue, createdAt, context);

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.MergePullRequestActivityAsync),
                new MergePrInput(agentContext.Repository, prRef.PullRequestNumber, commitMessage));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    $"## AI SDLC — Merged\n\nPR #{prRef.PullRequestNumber} merged after human approval. " +
                    "Launchcart CI/CD pipeline will deploy to test and production."));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:merged"));
        }

        var updatedAt = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

        return new WorkflowRun
        {
            RunId        = agentContext.RunId,
            Repository   = agentContext.Repository,
            Issue        = issue,
            PullRequest  = prRef,
            Status       = WorkflowRunStatus.Released,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt,
            RiskLevel    = ParseRiskLevel(riskResult.Decision),
            RiskDecision = riskDecision,
            Artefacts    = MapArtefacts(strategistResult, ownerResult, analystResult, architectResult,
                               securityResult, uxResult, devopsResult, complianceResult, contentResult,
                               analyticsResult, qaResult, coderResult, riskResult, releaseResult)
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GitHubIssueReference BuildIssueRef(AgentContext ctx) =>
        new(ctx.Repository, ctx.IssueNumber,
            $"https://github.com/{ctx.Repository}/issues/{ctx.IssueNumber}");

    private static WorkflowRun Stopped(string runId, GitHubIssueReference issue, DateTimeOffset createdAt, TaskOrchestrationContext ctx) =>
        new()
        {
            RunId        = runId,
            Repository   = issue.Repository,
            Issue        = issue,
            Status       = WorkflowRunStatus.Stopped,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = new DateTimeOffset(ctx.CurrentUtcDateTime, TimeSpan.Zero),
            RiskLevel    = RiskLevel.Unknown,
            RiskDecision = RiskDecision.Unknown.ToString()
        };

    private static WorkflowRun Failed(string runId, GitHubIssueReference issue, DateTimeOffset createdAt, TaskOrchestrationContext ctx, AgentResult riskResult) =>
        new()
        {
            RunId        = runId,
            Repository   = issue.Repository,
            Issue        = issue,
            Status       = WorkflowRunStatus.Failed,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = new DateTimeOffset(ctx.CurrentUtcDateTime, TimeSpan.Zero),
            RiskLevel    = RiskLevel.High,
            RiskDecision = RiskDecision.StopWorkflow.ToString(),
            Artefacts    = MapArtefacts(riskResult)
        };

    private static RiskLevel ParseRiskLevel(string? decision) => decision switch
    {
        "AUTO_MERGE_ELIGIBLE" => RiskLevel.Low,
        "BLOCKED"             => RiskLevel.High,
        _                     => RiskLevel.Medium
    };

    private static string GenerateBranchSlug(string issueTitle)
    {
        var slug = Regex.Replace(issueTitle.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }

    private static string BuildBriefComment(AgentResult result, int attempt)
    {
        var sb = new StringBuilder();
        if (attempt > 0)
        {
            sb.AppendLine($"> **Revised brief — attempt {attempt + 1} of {MaxBriefAttempts}**");
            sb.AppendLine();
        }
        sb.AppendLine("## AI SDLC — Refined Brief");
        sb.AppendLine();
        sb.AppendLine(GetContent(result));
        AppendLists(sb, result);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Reply `/approve-brief` to proceed or `/request-changes` with your feedback.");
        return sb.ToString();
    }

    private static string BuildSectionComment(string heading, AgentResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        sb.AppendLine(GetContent(result));
        AppendLists(sb, result);
        return sb.ToString();
    }

    private static string BuildSpecialistReviewsText(
        AgentResult security, AgentResult ux, AgentResult devops,
        AgentResult content, AgentResult compliance, AgentResult analytics)
    {
        var sb = new StringBuilder();
        AppendReviewSection(sb, "Security & Privacy",  security);
        AppendReviewSection(sb, "UX & Accessibility",  ux);
        AppendReviewSection(sb, "DevOps & Platform",   devops);
        AppendReviewSection(sb, "Content & SEO",       content);
        AppendReviewSection(sb, "Compliance & Legal",  compliance);
        AppendReviewSection(sb, "Data & Analytics",    analytics);
        return sb.ToString();
    }

    private static void AppendReviewSection(StringBuilder sb, string title, AgentResult result)
    {
        sb.AppendLine($"### {title}");
        sb.AppendLine();
        sb.AppendLine(GetContent(result));
        sb.AppendLine();
    }

    private static string BuildSpecialistReviewsComment(
        AgentResult security, AgentResult ux, AgentResult devops,
        AgentResult content, AgentResult compliance, AgentResult analytics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Specialist Reviews");
        sb.AppendLine();
        AppendCollapsible(sb, "Security & Privacy Review",   security);
        AppendCollapsible(sb, "UX & Accessibility Review",   ux);
        AppendCollapsible(sb, "DevOps & Platform Review",    devops);
        AppendCollapsible(sb, "Content & SEO Review",        content);
        AppendCollapsible(sb, "Compliance & Legal Review",   compliance);
        AppendCollapsible(sb, "Data & Analytics Review",     analytics);
        return sb.ToString();
    }

    private static string BuildImplementationComment(AgentResult qa, AgentResult coder)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Implementation Guidance");
        sb.AppendLine();
        AppendCollapsible(sb, "Test Plan",                   qa);
        AppendCollapsible(sb, "Implementation Specification", coder);
        return sb.ToString();
    }

    private static string BuildAutoMergedComment(int prNumber, AutoMergeEligibilityResult eligibility)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Auto-Merged");
        sb.AppendLine();
        sb.AppendLine($"> ✅ PR #{prNumber} merged automatically — all {eligibility.PassedGates.Count} gates passed.");
        sb.AppendLine();
        sb.AppendLine("Launchcart CI/CD pipeline will now deploy to test and production.");
        return sb.ToString();
    }

    private static string BuildGateResultsComment(int prNumber, AutoMergeEligibilityResult eligibility, string riskDecision, bool allowAutoMerge)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Merge Gate Results");
        sb.AppendLine();

        if (!allowAutoMerge)
            sb.AppendLine("> ℹ️ Auto-merge is not enabled for this repository (`allow_low_risk_auto_merge: false` in `.ai-sdlc.yml`) — human approval required.");
        else if (riskDecision != "AUTO_MERGE_ELIGIBLE")
            sb.AppendLine($"> ⚠️ Risk decision is `{riskDecision}` — human approval required before merge.");
        else
            sb.AppendLine($"> ⚠️ {eligibility.FailedGates.Count} gate(s) failed — human approval required.");

        if (eligibility.FailedGates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Failed gates");
            foreach (var gate in eligibility.FailedGates) sb.AppendLine($"- ❌ {gate}");
        }

        if (eligibility.PassedGates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Passed gates");
            foreach (var gate in eligibility.PassedGates) sb.AppendLine($"- ✅ {gate}");
        }

        sb.AppendLine();
        sb.AppendLine($"Reply `/approve-merge` on this issue to merge PR #{prNumber}.");
        return sb.ToString();
    }

    private static string BuildDevReadyComment(AgentResult release, string riskDecision)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Ready for Development");
        sb.AppendLine();

        if (riskDecision == "AUTO_MERGE_ELIGIBLE")
            sb.AppendLine("> ✅ **Risk level: LOW — eligible for auto-merge after all checks pass.**");
        else
            sb.AppendLine("> ⚠️ **Risk level: MEDIUM/HIGH — human review required before merge.**");

        sb.AppendLine();
        sb.AppendLine("### Release Documentation");
        sb.AppendLine();
        sb.AppendLine(GetContent(release));
        return sb.ToString();
    }

    private static string GetContent(AgentResult result) =>
        GetContent(result);

    private static void AppendCollapsible(StringBuilder sb, string title, AgentResult result)
    {
        sb.AppendLine($"<details><summary><strong>{title}</strong></summary>");
        sb.AppendLine();
        sb.AppendLine(GetContent(result));
        sb.AppendLine();
        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    private static void AppendLists(StringBuilder sb, AgentResult result)
    {
        if (result.FollowUpQuestions.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("### Questions for clarification");
            foreach (var q in result.FollowUpQuestions) sb.AppendLine($"- {q}");
        }
        if (result.BlockingIssues.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("### Blocking issues");
            foreach (var b in result.BlockingIssues) sb.AppendLine($"- {b}");
        }
    }

    private static IReadOnlyList<ArtefactReference> MapArtefacts(params AgentResult[] results) =>
        results
            .SelectMany(r => r.ArtefactsCreated.Select(a => new ArtefactReference(a, "generated", a)))
            .ToArray();
}
