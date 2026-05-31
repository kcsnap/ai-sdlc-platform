using System.Text;
using System.Text.RegularExpressions;
using AiSdlc.Agents;
using AiSdlc.GitHub;
using AiSdlc.GitHub.Webhooks;
using AiSdlc.RepoIndex;
using AiSdlc.RepoIndex.Charter;
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

        // ── Step 0: Fetch repo index + charter ────────────────────────────────
        var repoIndex = await context.CallActivityAsync<AiSdlc.RepoIndex.RepoIndex?>(
            nameof(AgentActivityFunctions.FetchRepoIndexAsync), agentContext.Repository);
        if (repoIndex is not null)
            agentContext.Metadata["repoContext"] = RepoIndexMarkdownRenderer.Render(repoIndex);
        var allowAutoMerge = ShouldAllowAutoMerge(
            repoFlag: repoIndex?.AllowLowRiskAutoMerge ?? false,
            mode:     agentContext.Mode);

        var charter = await context.CallActivityAsync<Charter?>(
            nameof(AgentActivityFunctions.FetchCharterAsync), agentContext.Repository);
        if (charter is not null)
            agentContext.Metadata["charter"] = CharterMarkdownRenderer.Render(charter);

        // ── Step 1: Product Strategist ─────────────────────────────────────────
        var strategistResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunProductStrategistAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["strategistOutput"] = strategistResult.ContextRef ?? strategistResult.OutputMarkdown ?? strategistResult.Summary;

        // ── Step 2: Product Owner — brief, auto-approved when allowAutoMerge ────
        AgentResult ownerResult;
        bool briefApproved;

        if (allowAutoMerge)
        {
            ownerResult = await context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunProductOwnerAsync), agentContext, AgentRetryOptions);

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                MakeBriefComment(agentContext.Repository, agentContext.IssueNumber, ownerResult, 0));

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
                    MakeBriefComment(agentContext.Repository, agentContext.IssueNumber, ownerResult, attempt));

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                    new AddLabelInput(agentContext.Repository, agentContext.IssueNumber,
                        "ai-sdlc:awaiting-brief-approval"));

                await RecordWorkflowAwaitAsync(context, agentContext,
                    "AwaitingBriefApproval", "Awaiting /approve-brief on the refined brief");

                using var cts = new CancellationTokenSource();
                var approveTask = context.WaitForExternalEvent<object?>(WorkflowEventNames.ApproveBrief,  cts.Token);
                var changesTask = context.WaitForExternalEvent<object?>(WorkflowEventNames.RequestChanges, cts.Token);
                var timeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(BriefApprovalTimeout), cts.Token);

                var winner = await Task.WhenAny(approveTask, changesTask, timeoutTask);
                cts.Cancel();

                if (winner == approveTask) { briefApproved = true; break; }
                if (winner == timeoutTask)
                {
                    await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Brief approval timed out");
                    return Stopped(agentContext.RunId, issue, createdAt, context);
                }
                if (attempt == MaxBriefAttempts - 1)
                {
                    await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Brief approval rejected after max attempts");
                    return Stopped(agentContext.RunId, issue, createdAt, context);
                }
            }
        }

        agentContext.Metadata["ownerBrief"] = ownerResult.ContextRef ?? ownerResult.OutputMarkdown ?? ownerResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.AddGitHubLabelAsync),
            new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:brief-approved"));

        // ── Step 3: Business Analyst ───────────────────────────────────────────
        var analystResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunBusinessAnalystAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["analystOutput"] = analystResult.ContextRef ?? analystResult.OutputMarkdown ?? analystResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                "AI SDLC — Business Analysis", analystResult));

        // ── Step 4: Architect ──────────────────────────────────────────────────
        var architectResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunArchitectAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["architectOutput"] = architectResult.ContextRef ?? architectResult.OutputMarkdown ?? architectResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                "AI SDLC — Architecture Review", architectResult));

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

        agentContext.Metadata["securityOutput"]   = securityResult.ContextRef   ?? securityResult.OutputMarkdown   ?? securityResult.Summary;
        agentContext.Metadata["uxOutput"]         = uxResult.ContextRef         ?? uxResult.OutputMarkdown         ?? uxResult.Summary;
        agentContext.Metadata["devopsOutput"]     = devopsResult.ContextRef     ?? devopsResult.OutputMarkdown     ?? devopsResult.Summary;
        agentContext.Metadata["contentOutput"]    = contentResult.ContextRef    ?? contentResult.OutputMarkdown    ?? contentResult.Summary;
        agentContext.Metadata["complianceOutput"] = complianceResult.ContextRef ?? complianceResult.OutputMarkdown ?? complianceResult.Summary;
        agentContext.Metadata["analyticsOutput"]  = analyticsResult.ContextRef  ?? analyticsResult.OutputMarkdown  ?? analyticsResult.Summary;

        // Post all specialist reviews as a single consolidated comment
        var specialistMarkdown = BuildSpecialistReviewsComment(
            securityResult, uxResult, devopsResult, contentResult, complianceResult, analyticsResult,
            out var specialistRefs);
        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                specialistMarkdown, specialistRefs.Count > 0 ? specialistRefs : null));

        // ── Step 6: QA + Senior Coder (parallel) ──────────────────────────────
        var qaTask     = context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunQaTestEngineerAsync), agentContext, AgentRetryOptions);
        var coderTask  = context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunSeniorCoderAsync),    agentContext, AgentRetryOptions);

        var qaResult    = await qaTask;
        var coderResult = await coderTask;

        agentContext.Metadata["testPlan"] = qaResult.ContextRef    ?? qaResult.OutputMarkdown    ?? qaResult.Summary;
        agentContext.Metadata["implSpec"] = coderResult.ContextRef ?? coderResult.OutputMarkdown ?? coderResult.Summary;

        var implGuidanceMarkdown = BuildImplementationComment(qaResult, coderResult, out var implGuidanceRefs);
        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                implGuidanceMarkdown, implGuidanceRefs.Count > 0 ? implGuidanceRefs : null));

        // ── Step 7: Risk Assessor ──────────────────────────────────────────────
        var riskResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunRiskAssessorAsync), agentContext, AgentRetryOptions);
        agentContext.Metadata["riskAssessment"] = riskResult.ContextRef ?? riskResult.OutputMarkdown ?? riskResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                "AI SDLC — Risk Assessment", riskResult));

        // ── Step 8: Route on risk decision ────────────────────────────────────
        var riskDecision = riskResult.Decision ?? "HUMAN_REVIEW_REQUIRED";

        if (riskDecision == "BLOCKED")
        {
            await RecordWorkflowExitAsync(context, agentContext, "Failed", "Risk assessment blocked the workflow");
            return Failed(agentContext.RunId, issue, createdAt, context, riskResult);
        }

        // Bootstrap runs (greenfield, no production traffic, no human reviewer)
        // promote any non-BLOCKED outcome to AUTO_MERGE_ELIGIBLE so the workflow
        // does not stall at the human-review gate.
        var overriddenDecision = ApplyBootstrapRiskOverride(riskDecision, agentContext.Mode);
        if (overriddenDecision != riskDecision)
        {
            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    "## AI SDLC — Risk Decision Override (Bootstrap)\n\n" +
                    $"Risk Assessor returned `{riskDecision}`. Bootstrap mode " +
                    "(greenfield repo, no production traffic) promotes all non-BLOCKED outcomes to " +
                    "`AUTO_MERGE_ELIGIBLE`. See the Risk Assessment comment above for the underlying analysis."));
            riskDecision = overriddenDecision;
        }

        if (riskDecision == "HUMAN_REVIEW_REQUIRED")
        {
            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:awaiting-human-review"));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    BuildHumanReviewRequiredComment(riskResult)));

            await RecordWorkflowAwaitAsync(context, agentContext,
                "AwaitingRiskApproval", "Awaiting /approve-release at the risk gate");

            using var cts = new CancellationTokenSource();
            var approveTask = context.WaitForExternalEvent<object?>(WorkflowEventNames.ApproveRelease, cts.Token);
            var changesTask = context.WaitForExternalEvent<object?>(WorkflowEventNames.RequestChanges,  cts.Token);
            var timeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(HumanReviewTimeout),   cts.Token);
            var winner      = await Task.WhenAny(approveTask, changesTask, timeoutTask);
            cts.Cancel();

            if (winner == timeoutTask)
            {
                await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Human review timed out for risk approval");
                return Stopped(agentContext.RunId, issue, createdAt, context);
            }
            if (winner == changesTask)
            {
                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        "## AI SDLC — Changes Requested\n\nPipeline paused. " +
                        "Amend the issue description with your changes, then close and reopen to restart the analysis from the beginning."));
                await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Changes requested by reviewer at risk gate");
                return Stopped(agentContext.RunId, issue, createdAt, context);
            }
        }

        // ── Step 9: Release Manager ────────────────────────────────────────────
        var releaseResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunReleaseManagerAsync), agentContext, AgentRetryOptions);

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            MakeDevReadyComment(agentContext.Repository, agentContext.IssueNumber, releaseResult, riskDecision));

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
                MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                    "AI SDLC — Implementation", implResult));

            // Resolve code content from blob when offloaded so CodeChangeParser can extract file blocks
            var implContent = implResult.OutputMarkdown;
            if (implContent is null && implResult.ContextRef is not null)
                implContent = await context.CallActivityAsync<string>(
                    nameof(AgentActivityFunctions.ResolveContextAsync), implResult.ContextRef);

            var fileChanges = CodeChangeParser.Parse(implContent);
            if (fileChanges.Count == 0)
            {
                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        "## AI SDLC — Implementation Failed\n\nThe code implementer did not produce any file changes. The pipeline cannot proceed automatically."));
                await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Code implementer produced no file changes");
                return Stopped(agentContext.RunId, issue, createdAt, context);
            }

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

            var commitMsg     = $"feat: {issueTitle} (closes #{agentContext.IssueNumber}) [ai-sdlc]";
            string? lastPath  = null;
            try
            {
                foreach (var file in fileChanges)
                {
                    lastPath = file.Path;
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.CommitFileAsync),
                        new CommitFileInput(agentContext.Repository, file.Path, file.Content, commitMsg, branchName));
                }
            }
            catch (TaskFailedException ex)
            {
                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        BuildCommitFailedComment(lastPath ?? "unknown", branchName, ex.InnerException?.Message ?? ex.Message)));
                await RecordWorkflowExitAsync(context, agentContext, "Stopped", $"CommitFileAsync failed on '{lastPath}': {ex.InnerException?.Message ?? ex.Message}");
                return Stopped(agentContext.RunId, issue, createdAt, context);
            }

            // ── Step 11b: Product Owner reviews committed content ──────────────
            var reviewFilePaths = fileChanges.Select(f => f.Path).ToArray();
            var branchReviewResult = await context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.ReviewBranchContentAsync),
                new ReviewBranchInput(
                    agentContext.RunId, agentContext.Repository, agentContext.IssueNumber,
                    branchName, reviewFilePaths,
                    agentContext.Metadata.GetValueOrDefault("ownerBrief")?.ToString()    ?? string.Empty,
                    agentContext.Metadata.GetValueOrDefault("analystOutput")?.ToString() ?? string.Empty,
                    agentContext.Metadata.GetValueOrDefault("charter")?.ToString()       ?? string.Empty),
                AgentRetryOptions);

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                    "AI SDLC — Implementation Review", branchReviewResult));

            if (branchReviewResult.Decision == "CHANGES_REQUIRED")
            {
                // Critical issues found — run CodeImplementer with PO feedback and recommit
                agentContext.Metadata["poReviewFeedback"] = branchReviewResult.ContextRef
                    ?? branchReviewResult.OutputMarkdown ?? branchReviewResult.Summary;

                var fixResult = await context.CallActivityAsync<AgentResult>(
                    nameof(AgentActivityFunctions.RunCodeImplementerAsync), agentContext, AgentRetryOptions);

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                        "AI SDLC — Implementation Fix", fixResult));

                var fixContent = fixResult.OutputMarkdown;
                if (fixContent is null && fixResult.ContextRef is not null)
                    fixContent = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.ResolveContextAsync), fixResult.ContextRef);

                var fixedChanges = CodeChangeParser.Parse(fixContent);
                if (fixedChanges.Count > 0)
                {
                    var fixCommitMsg = $"fix: address PO review feedback (closes #{agentContext.IssueNumber}) [ai-sdlc]";
                    foreach (var file in fixedChanges)
                    {
                        await context.CallActivityAsync(
                            nameof(AgentActivityFunctions.CommitFileAsync),
                            new CommitFileInput(agentContext.Repository, file.Path, file.Content, fixCommitMsg, branchName));
                    }

                    // Re-review the fixed content (once — no infinite loop)
                    var rereviewResult = await context.CallActivityAsync<AgentResult>(
                        nameof(AgentActivityFunctions.ReviewBranchContentAsync),
                        new ReviewBranchInput(
                            agentContext.RunId, agentContext.Repository, agentContext.IssueNumber,
                            branchName, fixedChanges.Select(f => f.Path).ToArray(),
                            agentContext.Metadata.GetValueOrDefault("ownerBrief")?.ToString()    ?? string.Empty,
                            agentContext.Metadata.GetValueOrDefault("analystOutput")?.ToString() ?? string.Empty,
                            agentContext.Metadata.GetValueOrDefault("charter")?.ToString()       ?? string.Empty),
                        AgentRetryOptions);

                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                        MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                            "AI SDLC — Implementation Re-Review", rereviewResult));

                    if (rereviewResult.Decision == "CHANGES_REQUIRED")
                    {
                        await context.CallActivityAsync(
                            nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                            new AddLabelInput(agentContext.Repository, agentContext.IssueNumber,
                                "ai-sdlc:implementation-review-failed"));
                        await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Implementation re-review still required changes");
                        return Stopped(agentContext.RunId, issue, createdAt, context);
                    }
                }
            }

            // Get branch HEAD SHA after all commits (including any fix cycle commits)
            var prHeadSha = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                new GetHeadShaInput(agentContext.Repository, branchName));

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
            {
                await RecordWorkflowExitAsync(context, agentContext, "Stopped", "PR-ready timed out");
                return Stopped(agentContext.RunId, issue, createdAt, context);
            }

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

        // Resolve release content to check for required documentation sections
        var releaseContent = releaseResult.OutputMarkdown;
        if (releaseContent is null && releaseResult.ContextRef is not null)
            releaseContent = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.ResolveContextAsync), releaseResult.ContextRef);

        var rollbackDocumented    = releaseContent?.Contains("rollback",    StringComparison.OrdinalIgnoreCase) ?? false;
        var releaseNotesGenerated = !string.IsNullOrWhiteSpace(releaseContent);
        var postDeployDefined     = releaseContent?.Contains("post-deploy", StringComparison.OrdinalIgnoreCase) ?? false;

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

        if (ShouldAutoMerge(riskDecision, eligibility.IsEligible, allowAutoMerge, agentContext.Mode))
        {
            // Either all 10 gates pass, or this is a Bootstrap run (greenfield — no production
            // to protect, downstream user-app CI/CD catches real failures).
            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.MergePullRequestActivityAsync),
                new MergePrInput(agentContext.Repository, prRef.PullRequestNumber, commitMessage));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    BuildAutoMergedComment(prRef.PullRequestNumber, eligibility, agentContext.Mode)));

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

            var gateFailureCount = eligibility.FailedGates.Count;
            var awaitSummary = gateFailureCount > 0
                ? $"Awaiting /approve-merge ({gateFailureCount} gate failure(s))"
                : "Awaiting /approve-merge";
            await RecordWorkflowAwaitAsync(context, agentContext, "AwaitingMergeApproval", awaitSummary);

            using var mergeCts      = new CancellationTokenSource();
            var approveTask         = context.WaitForExternalEvent<object?>(WorkflowEventNames.HumanReviewApproved, mergeCts.Token);
            var mergeTimeoutTask    = context.CreateTimer(context.CurrentUtcDateTime.Add(MergeApprovalTimeout), mergeCts.Token);
            var mergeWinner         = await Task.WhenAny(approveTask, mergeTimeoutTask);
            mergeCts.Cancel();

            if (mergeWinner == mergeTimeoutTask)
            {
                await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Merge approval timed out");
                return Stopped(agentContext.RunId, issue, createdAt, context);
            }

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.MergePullRequestActivityAsync),
                new MergePrInput(agentContext.Repository, prRef.PullRequestNumber, commitMessage));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    $"## AI SDLC — Merged\n\nPR #{prRef.PullRequestNumber} merged after human approval. " +
                    "The repository's CI/CD pipeline will now deploy this change."));

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

    // In Bootstrap mode, all non-BLOCKED risk outcomes auto-promote to AUTO_MERGE_ELIGIBLE.
    // BLOCKED is preserved because it signals something genuinely wrong (e.g. malformed spec)
    // that no amount of greenfield trust should override.
    public static string ApplyBootstrapRiskOverride(string riskDecision, WorkflowMode mode) =>
        mode == WorkflowMode.Bootstrap && riskDecision != "BLOCKED"
            ? "AUTO_MERGE_ELIGIBLE"
            : riskDecision;

    // Bootstrap runs are unattended by definition — they imply auto-merge regardless of
    // whether the user-app repo ships .ai-sdlc.yml. Propagates to the brief gate (Step 2),
    // the auto-PR path (Step 11) and the final merge gate (Step 12).
    public static bool ShouldAllowAutoMerge(bool repoFlag, WorkflowMode mode) =>
        repoFlag || mode == WorkflowMode.Bootstrap;

    // Final merge gate. Bootstrap (greenfield) bypasses the 10-gate eligibility check —
    // those gates protect launchcart's running production, but greenfield has none yet;
    // the user-app's own CI/CD catches real failures after the platform merges.
    public static bool ShouldAutoMerge(string riskDecision, bool eligibilityIsEligible,
                                       bool allowAutoMerge, WorkflowMode mode) =>
        riskDecision == "AUTO_MERGE_ELIGIBLE"
        && allowAutoMerge
        && (eligibilityIsEligible || mode == WorkflowMode.Bootstrap);

    // Writes a single Workflow-actor audit event before the orchestrator returns Stopped/Failed.
    // Outcome is "Stopped" or "Failed"; reason is a short human-readable string.
    private static Task RecordWorkflowExitAsync(
        TaskOrchestrationContext context, AgentContext agentContext, string outcome, string reason) =>
        context.CallActivityAsync(
            nameof(AgentActivityFunctions.RecordWorkflowExitAsync),
            new WorkflowExitAuditInput(agentContext.Repository, agentContext.IssueNumber, outcome, reason));

    // Emits a Workflow-actor audit event right before a WaitForExternalEvent so the dashboard
    // can surface the run as Blocked instead of misleadingly showing it as Running.
    // Action must start with "Awaiting" — that's the dashboard's recognition prefix.
    private static Task RecordWorkflowAwaitAsync(
        TaskOrchestrationContext context, AgentContext agentContext, string action, string reason) =>
        context.CallActivityAsync(
            nameof(AgentActivityFunctions.RecordWorkflowExitAsync),
            new WorkflowExitAuditInput(agentContext.Repository, agentContext.IssueNumber, action, reason));

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
        sb.AppendLine(ContentOrSentinel(result, "{C_BRIEF}"));
        AppendLists(sb, result);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Reply `/approve-brief` to proceed or `/request-changes` with your feedback.");
        return sb.ToString();
    }

    private static string BuildSectionComment(string heading, AgentResult result, string sentinel = "{C_BODY}")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        sb.AppendLine(ContentOrSentinel(result, sentinel));
        AppendLists(sb, result);
        return sb.ToString();
    }

    private static string BuildSpecialistReviewsComment(
        AgentResult security, AgentResult ux, AgentResult devops,
        AgentResult content, AgentResult compliance, AgentResult analytics,
        out Dictionary<string, string> contentRefs)
    {
        contentRefs = new();
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Specialist Reviews");
        sb.AppendLine();
        AppendCollapsible(sb, "Security & Privacy Review", security,    "{C_SEC}",        contentRefs);
        AppendCollapsible(sb, "UX & Accessibility Review", ux,          "{C_UX}",         contentRefs);
        AppendCollapsible(sb, "DevOps & Platform Review",  devops,      "{C_DEVOPS}",     contentRefs);
        AppendCollapsible(sb, "Content & SEO Review",       content,     "{C_CONTENT}",    contentRefs);
        AppendCollapsible(sb, "Compliance & Legal Review",  compliance,  "{C_COMPLIANCE}", contentRefs);
        AppendCollapsible(sb, "Data & Analytics Review",    analytics,   "{C_ANALYTICS}",  contentRefs);
        return sb.ToString();
    }

    private static string BuildImplementationComment(AgentResult qa, AgentResult coder,
        out Dictionary<string, string> contentRefs)
    {
        contentRefs = new();
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Implementation Guidance");
        sb.AppendLine();
        AppendCollapsible(sb, "Test Plan",                    qa,    "{C_QA}",    contentRefs);
        AppendCollapsible(sb, "Implementation Specification", coder, "{C_CODER}", contentRefs);
        return sb.ToString();
    }

    private static string BuildCommitFailedComment(string path, string branchName, string errorDetail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Commit Failed");
        sb.AppendLine();
        sb.AppendLine($"Unable to commit `{path}` to branch `{branchName}`.");
        sb.AppendLine();
        if (path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("> ⚠️ **Workflow file detected.** Writing to `.github/workflows/` requires the `workflow` scope on classic PATs, or the **Workflows** permission on fine-grained PATs.");
            sb.AppendLine();
        }
        sb.AppendLine($"**Error:** `{errorDetail}`");
        sb.AppendLine();
        sb.AppendLine("Fix the token permissions in Key Vault, then close and reopen this issue to retry.");
        return sb.ToString();
    }

    private static string BuildHumanReviewRequiredComment(AgentResult riskResult)
    {
        var level = riskResult.Summary?.Contains("Level: HIGH",   StringComparison.OrdinalIgnoreCase) == true ? "HIGH"
                  : riskResult.Summary?.Contains("Level: MEDIUM", StringComparison.OrdinalIgnoreCase) == true ? "MEDIUM"
                  : "MEDIUM/HIGH";

        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Your Decision Required");
        sb.AppendLine();
        sb.AppendLine($"> ⚠️ **Risk level: {level}** — the AI needs your sign-off before writing any code.");
        sb.AppendLine();
        sb.AppendLine("### What to review");
        sb.AppendLine("Scroll up through the comments and check:");
        sb.AppendLine("- **Risk Assessment** — why this was flagged and what concerns were raised");
        sb.AppendLine("- **Specialist Reviews** — security, compliance, DevOps details");
        sb.AppendLine("- **Implementation Guidance** — what the AI plans to build");
        sb.AppendLine();

        if (riskResult.BlockingIssues is { Count: > 0 })
        {
            sb.AppendLine("### Blocking issues");
            foreach (var issue in riskResult.BlockingIssues)
                sb.AppendLine($"- {issue}");
            sb.AppendLine();
        }

        sb.AppendLine("### Next steps");
        sb.AppendLine();
        sb.AppendLine("**To proceed →** reply `/approve-release`");
        sb.AppendLine("The AI will write the code and open a PR. You will get a second prompt (`/approve-merge`) before anything merges to main.");
        sb.AppendLine();
        sb.AppendLine("**To request changes →** reply `/request-changes` — the pipeline will pause so you can update the issue description with your amendments, then close and reopen to restart the analysis.");
        sb.AppendLine();
        sb.AppendLine("**To stop →** close this issue or do nothing — the pipeline expires after 14 days.");
        return sb.ToString();
    }

    private static string BuildAutoMergedComment(int prNumber, AutoMergeEligibilityResult eligibility, WorkflowMode mode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Auto-Merged");
        sb.AppendLine();

        if (mode == WorkflowMode.Bootstrap && !eligibility.IsEligible)
        {
            sb.AppendLine($"> ✅ PR #{prNumber} merged automatically (Bootstrap policy).");
            sb.AppendLine();
            sb.AppendLine($"Bootstrap runs bypass the {eligibility.PassedGates.Count + eligibility.FailedGates.Count}-gate eligibility check — greenfield repos have no running production to protect. The user-app's own CI/CD will catch real failures.");
            sb.AppendLine();
            sb.AppendLine($"Gates that would have failed in Standard mode (informational only):");
            foreach (var gate in eligibility.FailedGates) sb.AppendLine($"- ⏭ {gate}");
        }
        else
        {
            sb.AppendLine($"> ✅ PR #{prNumber} merged automatically — all {eligibility.PassedGates.Count} gates passed.");
        }

        sb.AppendLine();
        sb.AppendLine("The repository's CI/CD pipeline will now deploy this change.");
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
        sb.AppendLine("### Next steps");
        sb.AppendLine();
        sb.AppendLine($"**To merge →** reply `/approve-merge`");
        sb.AppendLine($"PR #{prNumber} will be merged to main and the issue closed.");
        sb.AppendLine();
        sb.AppendLine("**To stop →** close this issue or do nothing — the pipeline expires after 14 days.");
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
        sb.AppendLine(ContentOrSentinel(release, "{C_RELEASE}"));
        return sb.ToString();
    }

    private static PostCommentInput MakeSectionComment(string repo, int issue, string heading, AgentResult result)
    {
        const string Sentinel = "{C_BODY}";
        var refs = result.ContextRef is not null
            ? new Dictionary<string, string> { [Sentinel] = result.ContextRef }
            : null;
        return new PostCommentInput(repo, issue, BuildSectionComment(heading, result, Sentinel), refs);
    }

    private static PostCommentInput MakeBriefComment(string repo, int issue, AgentResult result, int attempt)
    {
        const string Sentinel = "{C_BRIEF}";
        var refs = result.ContextRef is not null
            ? new Dictionary<string, string> { [Sentinel] = result.ContextRef }
            : null;
        return new PostCommentInput(repo, issue, BuildBriefComment(result, attempt), refs);
    }

    private static PostCommentInput MakeDevReadyComment(string repo, int issue, AgentResult release, string riskDecision)
    {
        const string Sentinel = "{C_RELEASE}";
        var refs = release.ContextRef is not null
            ? new Dictionary<string, string> { [Sentinel] = release.ContextRef }
            : null;
        return new PostCommentInput(repo, issue, BuildDevReadyComment(release, riskDecision), refs);
    }

    private static string ContentOrSentinel(AgentResult result, string sentinel) =>
        result.OutputMarkdown ?? (result.ContextRef is not null ? sentinel : result.Summary);

    private static void AppendCollapsible(StringBuilder sb, string title, AgentResult result,
        string sentinel, Dictionary<string, string> contentRefs)
    {
        if (result.ContextRef is not null) contentRefs[sentinel] = result.ContextRef;
        sb.AppendLine($"<details><summary><strong>{title}</strong></summary>");
        sb.AppendLine();
        sb.AppendLine(ContentOrSentinel(result, sentinel));
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
