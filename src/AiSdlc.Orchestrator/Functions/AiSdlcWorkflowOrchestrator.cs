using System.Text;
using System.Text.RegularExpressions;
using AiSdlc.Agents;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.GitHub;
using AiSdlc.GitHub.Webhooks;
using AiSdlc.RepoIndex;
using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;
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

    // How long a stalled stage stays resumable (waiting for /retry) before the run gives up.
    private static readonly TimeSpan StageRetryWindow = TimeSpan.FromDays(7);

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
        {
            agentContext.Metadata["charter"] = CharterMarkdownRenderer.Render(charter);
            // Selects the Code Implementer's Scaffold Contract variant: Yorrixx seeds the no-auth shell
            // when NeedsAuth == false, so the contract must match. Stored as a string so it survives
            // Durable activity (de)serialisation. HELD behind the lockstep gate until the no-auth
            // template variant is live (docs/roadmap/conditional-auth-yorrixx-brief.md §6).
            agentContext.Metadata["needsAuth"] = charter.Constraints.NeedsAuth ? "true" : "false";
        }

        // Stack profile (derive-once-stamp): Yorrixx stamps .yorrixx/profile.json at seed time; the
        // platform reads it and never re-derives. Drives the Static posture + Code Implementer contract
        // (docs/roadmap/stack-profiles-static-first.md). Absent/malformed → "FullStack" — inert until a
        // Static template + profile.json seeding ship, so it's safe ahead of that.
        var stackProfile = await context.CallActivityAsync<string>(
            nameof(AgentActivityFunctions.FetchStackProfileAsync), agentContext.Repository);
        agentContext.Metadata["stackProfile"] = stackProfile;

        // Capability profile — persistence axis (api-only vs api+db). The database need is agent-derived
        // (Balanced); a payments app forces it on (CapabilityResolver invariant). Only meaningful for a
        // FullStack app with a charter — a Static app has no API/DB, and without a charter we keep today's
        // DB-backed default. Honored-but-flagged gaps (e.g. payments without email) surface at review.
        // Inert until the api-only template variant ships (mirrors how needsAuth landed ahead of seeding).
        if (charter is not null && !string.Equals(stackProfile, "Static", StringComparison.OrdinalIgnoreCase))
        {
            var databaseDerived = await context.CallActivityAsync<bool>(
                nameof(AgentActivityFunctions.DeriveDatabaseNeedAsync),
                CharterMarkdownRenderer.Render(charter));
            var profile = CapabilityResolver.Resolve(charter, databaseDerived);
            agentContext.Metadata["needsDatabase"] = profile.Database ? "true" : "false";

            var gaps = CapabilityResolver.DetectGaps(charter);
            if (gaps.Count > 0)
                agentContext.Metadata["capabilityGaps"] =
                    string.Join("\n", gaps.Select(g => $"- {g.Capability}: {g.Reason}"));
        }

        // Real photography for a Static marketing page — a design-director judgment decides per brand
        // (default none), then real Pexels URLs are fetched. FullStack imagery waits on the marketing-
        // landing slot (held). Empty / no PexelsApiKey configured → generative-only (the safe default).
        if (charter is not null && string.Equals(stackProfile, "Static", StringComparison.OrdinalIgnoreCase))
        {
            var imageryManifest = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.DeriveImageryAsync),
                CharterMarkdownRenderer.Render(charter));
            if (!string.IsNullOrWhiteSpace(imageryManifest))
                agentContext.Metadata["imageryManifest"] = imageryManifest;
        }

        // A reopened issue means the previous run's release failed downstream verification
        // (Yorrixx posts findings as comments, then reopens). Surface those findings to every
        // agent so the re-run fixes them instead of regenerating blind (#88).
        if (string.Equals(agentContext.Metadata.GetValueOrDefault("reopened")?.ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            var findings = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.FetchReopenFindingsAsync),
                new FetchReopenFindingsInput(agentContext.Repository, agentContext.IssueNumber));
            if (!string.IsNullOrWhiteSpace(findings))
            {
                agentContext.Metadata["reopenFindings"] = findings;

                // Repair runs iterate on the released code instead of regenerating it — six
                // cycles on user-app-624d97a2 never converged because each rewrite introduced
                // fresh defects in different files (#92). The bundle lives in the context
                // store; the metadata carries only the ref, resolved at agent execution.
                var existingSourceRef = await context.CallActivityAsync<string>(
                    nameof(AgentActivityFunctions.FetchExistingSourceAsync),
                    new FetchExistingSourceInput(agentContext.RunId, agentContext.Repository, FindingsText: findings));
                if (!string.IsNullOrWhiteSpace(existingSourceRef))
                    agentContext.Metadata["existingSource"] = existingSourceRef;
            }
        }

        // ── Step 1: Product Strategist ─────────────────────────────────────────
        var strategistResult = await RunStageWithRecoveryAsync(context, agentContext, "Product Strategist",
            () => context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunProductStrategistAsync), agentContext, AgentRetryOptions));
        agentContext.Metadata["strategistOutput"] = strategistResult.ContextRef ?? strategistResult.OutputMarkdown ?? strategistResult.Summary;

        // ── Step 2: Product Owner — brief, auto-approved when allowAutoMerge ────
        AgentResult ownerResult;
        bool briefApproved;

        if (allowAutoMerge)
        {
            ownerResult = await RunStageWithRecoveryAsync(context, agentContext, "Product Owner",
                () => context.CallActivityAsync<AgentResult>(
                    nameof(AgentActivityFunctions.RunProductOwnerAsync), agentContext, AgentRetryOptions));

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
        var analystResult = await RunStageWithRecoveryAsync(context, agentContext, "Business Analyst",
            () => context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunBusinessAnalystAsync), agentContext, AgentRetryOptions));
        agentContext.Metadata["analystOutput"] = analystResult.ContextRef ?? analystResult.OutputMarkdown ?? analystResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                "AI SDLC — Business Analysis", analystResult));

        // ── Step 4: Architect ──────────────────────────────────────────────────
        var architectResult = await RunStageWithRecoveryAsync(context, agentContext, "Architect",
            () => context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunArchitectAsync), agentContext, AgentRetryOptions));
        agentContext.Metadata["architectOutput"] = architectResult.ContextRef ?? architectResult.OutputMarkdown ?? architectResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                "AI SDLC — Architecture Review", architectResult));

        // ── Step 5: Parallel specialist reviews (fan-out) ─────────────────────
        // Wrapped as a single recoverable stage: a /retry after failure re-runs all six.
        var reviewResults = await RunStageWithRecoveryAsync(context, agentContext, "Specialist Reviews",
            () => Task.WhenAll(new List<Task<AgentResult>>
            {
                context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunSecurityPrivacyReviewerAsync), agentContext, AgentRetryOptions),
                context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunUxAccessibilityReviewerAsync), agentContext, AgentRetryOptions),
                context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunDevOpsPlatformEngineerAsync),  agentContext, AgentRetryOptions),
                context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunContentSeoReviewerAsync),      agentContext, AgentRetryOptions),
                context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunComplianceLegalReviewerAsync), agentContext, AgentRetryOptions),
                context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunDataAnalyticsReviewerAsync),   agentContext, AgentRetryOptions),
            }));

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
        var (qaResult, coderResult) = await RunStageWithRecoveryAsync(context, agentContext, "QA + Senior Coder",
            async () =>
            {
                var qaTask    = context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunQaTestEngineerAsync), agentContext, AgentRetryOptions);
                var coderTask = context.CallActivityAsync<AgentResult>(nameof(AgentActivityFunctions.RunSeniorCoderAsync),    agentContext, AgentRetryOptions);
                return (Qa: await qaTask, Coder: await coderTask);
            });

        agentContext.Metadata["testPlan"] = qaResult.ContextRef    ?? qaResult.OutputMarkdown    ?? qaResult.Summary;
        agentContext.Metadata["implSpec"] = coderResult.ContextRef ?? coderResult.OutputMarkdown ?? coderResult.Summary;

        var implGuidanceMarkdown = BuildImplementationComment(qaResult, coderResult, out var implGuidanceRefs);
        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                implGuidanceMarkdown, implGuidanceRefs.Count > 0 ? implGuidanceRefs : null));

        // ── Step 7: Risk Assessor ──────────────────────────────────────────────
        var riskResult = await RunStageWithRecoveryAsync(context, agentContext, "Risk Assessor",
            () => context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunRiskAssessorAsync), agentContext, AgentRetryOptions));
        agentContext.Metadata["riskAssessment"] = riskResult.ContextRef ?? riskResult.OutputMarkdown ?? riskResult.Summary;

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                "AI SDLC — Risk Assessment", riskResult));

        // ── Step 8: Route on risk decision ────────────────────────────────────
        var riskDecision = riskResult.Decision ?? "HUMAN_REVIEW_REQUIRED";

        // Bootstrap runs (greenfield, no production traffic, no human reviewer) promote EVERY
        // outcome — including BLOCKED — to AUTO_MERGE_ELIGIBLE. The AI risk assessment is
        // non-deterministic and was BLOCKing greenfield MVPs on aspirational compliance (no
        // published privacy policy / GDPR framework). That concern is now addressed responsibly:
        // the platform injects templated Privacy Policy + Terms of Service (each with a "not
        // production ready — review before going public" disclaimer) into every build, so a
        // missing-legal-docs BLOCK is no longer valid. The CI gate and Yorrixx's verification
        // suite remain the real safety nets. Override BEFORE the BLOCKED hard-exit so it applies.
        var overriddenDecision = ApplyBootstrapRiskOverride(riskDecision, agentContext.Mode);
        if (overriddenDecision != riskDecision)
        {
            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    "## AI SDLC — Risk Decision Override (Bootstrap)\n\n" +
                    $"Risk Assessor returned `{riskDecision}`. Bootstrap mode " +
                    "(greenfield repo, no production traffic, no human reviewer) promotes all outcomes to " +
                    "`AUTO_MERGE_ELIGIBLE`. Templated legal docs are injected into every build, and CI + the " +
                    "verification gate guard the merge. See the Risk Assessment comment above for the analysis."));
            riskDecision = overriddenDecision;
        }

        if (riskDecision == "BLOCKED")
        {
            await RecordWorkflowExitAsync(context, agentContext, "Failed", "Risk assessment blocked the workflow");
            return Failed(agentContext.RunId, issue, createdAt, context, riskResult);
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
        var releaseResult = await RunStageWithRecoveryAsync(context, agentContext, "Release Manager",
            () => context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunReleaseManagerAsync), agentContext, AgentRetryOptions));

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
            // ── Step 9b: Resume the newest open ai/ PR instead of rerolling ────
            // Yorrixx retries blocked builds by filing NEW issues, so a fresh run used to
            // regenerate from the charter while a nearly-green PR sat open (~30 trivial
            // type errors discarded for 115 new ones on user-app-624d97a2). Bootstrap runs
            // now prime repair mode from that PR's branch source and its failing-check
            // findings, and commit on top of its branch (#98).
            //
            // Resume takes PRECEDENCE over reopen findings (#100): an open ai/ PR with CI
            // failures is unreleased work-in-progress — post-release verification findings
            // can't apply to code that never merged, and a reopen-triggered comment scraped
            // as "findings" once made the reopen path force-reset the PR's branch, auto-
            // closing it and wiping a converged fix. When no open PR exists, the reopen
            // metadata set in Step 0 drives the repair exactly as before.
            OpenPullRequestInfo? resumePr = null;
            if (agentContext.Mode == WorkflowMode.Bootstrap)
            {
                resumePr = await context.CallActivityAsync<OpenPullRequestInfo?>(
                    nameof(AgentActivityFunctions.GetNewestOpenAiPrAsync), agentContext.Repository);

                if (resumePr is not null)
                {
                    var resumeFindingsRef = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.FetchCiFailureFindingsAsync),
                        new FetchCiFindingsInput(agentContext.RunId, agentContext.Repository, resumePr.HeadSha, Attempt: 0));
                    var resumeSourceRef = string.IsNullOrWhiteSpace(resumeFindingsRef)
                        ? string.Empty
                        : await context.CallActivityAsync<string>(
                            nameof(AgentActivityFunctions.FetchExistingSourceAsync),
                            new FetchExistingSourceInput(agentContext.RunId, agentContext.Repository, resumePr.HeadBranch));

                    if (!string.IsNullOrWhiteSpace(resumeFindingsRef) && !string.IsNullOrWhiteSpace(resumeSourceRef))
                    {
                        agentContext.Metadata["ciFindings"]     = resumeFindingsRef;
                        agentContext.Metadata["existingSource"] = resumeSourceRef;
                    }
                    else
                    {
                        resumePr = null; // nothing actionable on the open PR — regenerate fresh
                    }
                }
            }
            var resumeMode = resumePr is not null;

            // ── Step 10: Generate code implementation ─────────────────────────
            // Template-first Static (env-gated): a fresh Static build fills a pre-built template with a
            // cheap model instead of generating from scratch on Opus. If that activity fails (no fitting
            // template, bad model output → assembly throws), fall back to the Code Implementer — never
            // worse than today. Repairs and FullStack always use the Code Implementer.
            var useStaticTemplate =
                !resumeMode
                && !AgentActivityFunctions.IsRepairRun(agentContext.Metadata)
                && AgentActivityFunctions.StaticTemplateFirstEnabled
                && string.Equals(
                    Convert.ToString(agentContext.Metadata.GetValueOrDefault("stackProfile")),
                    "Static", StringComparison.OrdinalIgnoreCase);

            AgentResult implResult;
            var templateBuilt = false;
            if (useStaticTemplate)
            {
                try
                {
                    implResult = await context.CallActivityAsync<AgentResult>(
                        nameof(AgentActivityFunctions.RunStaticTemplateBuilderAsync), agentContext, AgentRetryOptions);
                    templateBuilt = true;
                }
                catch (TaskFailedException)
                {
                    implResult = await RunStageWithRecoveryAsync(context, agentContext, "Code Implementer",
                        () => context.CallActivityAsync<AgentResult>(
                            nameof(AgentActivityFunctions.RunCodeImplementerAsync), agentContext, AgentRetryOptions));
                }
            }
            else
            {
                implResult = await RunStageWithRecoveryAsync(context, agentContext, "Code Implementer",
                    () => context.CallActivityAsync<AgentResult>(
                        nameof(AgentActivityFunctions.RunCodeImplementerAsync), agentContext, AgentRetryOptions));
            }

            // The implementation comment is posted AFTER the commits (step 11) as a summary with
            // branch + SHA — code lives only on the branch, never in issue comments (#84).

            // Resolve code content from blob when offloaded so CodeChangeParser can extract file blocks
            var implContent = implResult.OutputMarkdown;
            if (implContent is null && implResult.ContextRef is not null)
                implContent = await context.CallActivityAsync<string>(
                    nameof(AgentActivityFunctions.ResolveContextAsync), implResult.ContextRef);

            var fileChanges = CodeChangeParser.Parse(implContent)
                .Where(f => !AgentActivityFunctions.IsProtectedPath(f.Path)) // .github/ is Yorrixx-owned
                // Q1(b): a generated acceptance spec with known-bad content (invalid Playwright API /
                // wrong form endpoint) is rejected — the stubs stay, e2e fails, and the repair re-authors.
                .Where(f => !AgentActivityFunctions.IsRejectedAcceptanceSpec(f, existingAcceptanceSpec: null, isRepair: false))
                .Where(f => !AgentActivityFunctions.ContainsRedactionEcho(f)) // never commit echoed redaction masks
                .Where(f => !GeneratedHtmlLint.IsRejectedGeneratedHtml(f))    // D8: no tag-soup HTML reaches the repo
                .ToList();

            // Any repair run — resume (open PR), in-run CI, OR a reopened-issue verification
            // repair — must enforce minimality against its findings so a "repair" can't smuggle
            // in a refactor or a full regeneration (#100: a reopen regenerated 412 errors over a
            // green API). ciFindings (a context-store ref) takes precedence over reopenFindings
            // (inline text); resume mode primes ciFindings, so this covers it too.
            if (AgentActivityFunctions.IsRepairRun(agentContext.Metadata))
            {
                string repairFindingsText;
                if (agentContext.Metadata.TryGetValue("ciFindings", out var ciRef)
                    && !string.IsNullOrWhiteSpace(Convert.ToString(ciRef)))
                {
                    repairFindingsText = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.ResolveContextAsync), Convert.ToString(ciRef)!);
                }
                else
                {
                    repairFindingsText = Convert.ToString(agentContext.Metadata.GetValueOrDefault("reopenFindings")) ?? string.Empty;
                }

                // acceptance.spec.ts may be MAINTAINED by a repair but never GUTTED (#117): pass its
                // existing content (from the source bundle) so the filter drops any regressive change.
                string? existingAcceptanceSpec = null;
                if (agentContext.Metadata.TryGetValue("existingSource", out var srcRef)
                    && !string.IsNullOrWhiteSpace(Convert.ToString(srcRef)))
                {
                    var existingSourceText = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.ResolveContextAsync), Convert.ToString(srcRef)!);
                    existingAcceptanceSpec = CodeChangeParser.Parse(existingSourceText)
                        .FirstOrDefault(f => AgentActivityFunctions.IsAcceptanceSpec(f.Path))?.Content;
                }
                fileChanges = AgentActivityFunctions.FilterRepairChanges(fileChanges, repairFindingsText, existingAcceptanceSpec);
            }

            if (fileChanges.Count == 0)
            {
                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        "## AI SDLC — Implementation Failed\n\nThe code implementer did not produce any file changes. The pipeline cannot proceed automatically."));
                await RecordWorkflowExitAsync(context, agentContext, "Stopped", "Code implementer produced no file changes");
                return Stopped(agentContext.RunId, issue, createdAt, context);
            }

            // Pre-commit email-leak guard: a literal contact email in generated output is a leak
            // (the model invented one instead of the deploy-substituted placeholder). SANITIZE it —
            // rewrite to __CONTACT_EMAIL__ (ramp wave-1: stopping the build over an invented
            // your@email.com parked an otherwise-good run). Pure regex, deterministic.
            var emailLeaks = EmailLeakGuard.Scan(fileChanges);
            if (emailLeaks.Count > 0)
            {
                fileChanges = EmailLeakGuard.Sanitize(fileChanges).ToList();
                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        BuildEmailSanitizedComment(emailLeaks)));
            }

            // ── Step 11: Create branch and commit files ────────────────────────
            var slug          = GenerateBranchSlug(issueTitle);
            var branchName    = resumeMode ? resumePr!.HeadBranch : $"ai/{agentContext.IssueNumber}-{slug}";

            var defaultBranch = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.GetDefaultBranchNameActivityAsync),
                agentContext.Repository);

            if (!resumeMode)
            {
                // Fresh run: branch starts from current main (force-reset if stale). Resume
                // mode must NOT reset — the open PR's commits are the code being repaired.
                var headSha = await context.CallActivityAsync<string>(
                    nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                    new GetHeadShaInput(agentContext.Repository, defaultBranch));

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.CreateBranchActivityAsync),
                    new CreateBranchInput(agentContext.Repository, branchName, headSha));
            }

            var commitMsg = resumeMode
                ? $"fix: repair CI failures on PR #{resumePr!.Number} (closes #{agentContext.IssueNumber}) [ai-sdlc]"
                : $"feat: {issueTitle} (closes #{agentContext.IssueNumber}) [ai-sdlc]";
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

                // Inject the reusable legal-document templates into EVERY build (deterministic —
                // static content from the platform, no AI generation, so it costs no tokens). Their
                // guaranteed presence is what lets the risk gate stop blocking greenfield MVPs on
                // missing privacy policy / GDPR docs. Re-committing identical content on a resume is
                // a no-op. The site footer links these (Code Implementer auth/legal contract).
                foreach (var doc in LegalDocumentTemplates.All)
                {
                    lastPath = doc.Path;
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.CommitFileAsync),
                        new CommitFileInput(agentContext.Repository, doc.Path, doc.Content, commitMsg, branchName));
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

            var implCommitSha = await context.CallActivityAsync<string>(
                nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                new GetHeadShaInput(agentContext.Repository, branchName));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                    BuildImplementationSummaryComment("AI SDLC — Implementation", implResult.Summary,
                        agentContext.Repository, defaultBranch, branchName, implCommitSha, fileChanges.Count)));

            // ── Step 11b: Product Owner reviews committed content ──────────────
            // SKIPPED for ALL repair runs — resume (#102) AND reopen-repair (#118): the review
            // judges the repaired files against the full charter brief, so a narrow fix gets
            // flagged for "missing" features/components that already exist elsewhere (v003: an
            // AuthGate fix rejected for not "implementing the acceptance tests"), and the run
            // dies before the CI gate. A repair is a surgical fix of an already-reviewed app;
            // like the in-gate repair loop (Step 12a), CI is its sole arbiter. Fresh first
            // builds are still reviewed.
            //
            // ALSO SKIPPED for template-first builds (w1proof0): the review judges platform
            // conventions it cannot know — it flagged the deploy-substituted __CONTACT_EMAIL__
            // token and the platform-injected legal pages as CRITICAL, and its fix loop re-
            // generated deterministic template output through the LLM (which corrupted it via
            // redaction echo). Template output is pre-validated by the shipped acceptance suite;
            // the scaffold gate + verification are its arbiters.
            if (!resumeMode && !templateBuilt && !AgentActivityFunctions.IsRepairRun(agentContext.Metadata))
            {
                var reviewFilePaths = fileChanges.Select(f => f.Path).ToArray();
                var branchReviewResult = await RunStageWithRecoveryAsync(context, agentContext, "Implementation Review",
                    () => context.CallActivityAsync<AgentResult>(
                        nameof(AgentActivityFunctions.ReviewBranchContentAsync),
                        new ReviewBranchInput(
                            agentContext.RunId, agentContext.Repository, agentContext.IssueNumber,
                            branchName, reviewFilePaths,
                            agentContext.Metadata.GetValueOrDefault("ownerBrief")?.ToString()    ?? string.Empty,
                            agentContext.Metadata.GetValueOrDefault("analystOutput")?.ToString() ?? string.Empty,
                            agentContext.Metadata.GetValueOrDefault("charter")?.ToString()       ?? string.Empty),
                        AgentRetryOptions));

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    MakeSectionComment(agentContext.Repository, agentContext.IssueNumber,
                        "AI SDLC — Implementation Review", branchReviewResult));

                if (branchReviewResult.Decision == "CHANGES_REQUIRED")
                {
                    // Critical issues found — run CodeImplementer with PO feedback and recommit
                    agentContext.Metadata["poReviewFeedback"] = branchReviewResult.ContextRef
                        ?? branchReviewResult.OutputMarkdown ?? branchReviewResult.Summary;

                    var fixResult = await RunStageWithRecoveryAsync(context, agentContext, "Code Implementer (fix)",
                        () => context.CallActivityAsync<AgentResult>(
                            nameof(AgentActivityFunctions.RunCodeImplementerAsync), agentContext, AgentRetryOptions));

                    var fixContent = fixResult.OutputMarkdown;
                    if (fixContent is null && fixResult.ContextRef is not null)
                        fixContent = await context.CallActivityAsync<string>(
                            nameof(AgentActivityFunctions.ResolveContextAsync), fixResult.ContextRef);

                    // This PO-fix loop only runs on a fresh first build (#121 skips review for
                    // repair runs), so acceptance.spec.ts authoring is allowed — only the always
                    // immutable harness (.github/, tests/e2e/ except acceptance.spec.ts) is dropped.
                    var fixedChanges = CodeChangeParser.Parse(fixContent)
                        .Where(f => !AgentActivityFunctions.IsProtectedPath(f.Path))
                        .Where(f => !AgentActivityFunctions.ContainsRedactionEcho(f)) // w1proof0: echoed masks corrupted SVGs
                        .Where(f => !GeneratedHtmlLint.IsRejectedGeneratedHtml(f))    // D8
                        .ToList();
                    var fixLeaks = EmailLeakGuard.Scan(fixedChanges);
                    if (fixLeaks.Count > 0)
                    {
                        fixedChanges = EmailLeakGuard.Sanitize(fixedChanges).ToList();
                        await context.CallActivityAsync(
                            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                                BuildEmailSanitizedComment(fixLeaks)));
                    }

                    if (fixedChanges.Count > 0)
                    {
                        var fixCommitMsg = $"fix: address PO review feedback (closes #{agentContext.IssueNumber}) [ai-sdlc]";
                        foreach (var file in fixedChanges)
                        {
                            await context.CallActivityAsync(
                                nameof(AgentActivityFunctions.CommitFileAsync),
                                new CommitFileInput(agentContext.Repository, file.Path, file.Content, fixCommitMsg, branchName));
                        }

                        var fixCommitSha = await context.CallActivityAsync<string>(
                            nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                            new GetHeadShaInput(agentContext.Repository, branchName));

                        await context.CallActivityAsync(
                            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                                BuildImplementationSummaryComment("AI SDLC — Implementation Fix", fixResult.Summary,
                                    agentContext.Repository, defaultBranch, branchName, fixCommitSha, fixedChanges.Count)));

                        // Re-review the fixed content (once — no infinite loop)
                        var rereviewResult = await RunStageWithRecoveryAsync(context, agentContext, "Implementation Re-Review",
                            () => context.CallActivityAsync<AgentResult>(
                                nameof(AgentActivityFunctions.ReviewBranchContentAsync),
                                new ReviewBranchInput(
                                    agentContext.RunId, agentContext.Repository, agentContext.IssueNumber,
                                    branchName, fixedChanges.Select(f => f.Path).ToArray(),
                                    agentContext.Metadata.GetValueOrDefault("ownerBrief")?.ToString()    ?? string.Empty,
                                    agentContext.Metadata.GetValueOrDefault("analystOutput")?.ToString() ?? string.Empty,
                                    agentContext.Metadata.GetValueOrDefault("charter")?.ToString()       ?? string.Empty),
                                AgentRetryOptions));

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
                    else
                    {
                        await context.CallActivityAsync(
                            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                                "## AI SDLC — Implementation Fix\n\nThe fix attempt produced no file changes; proceeding with the original implementation."));
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

            // ── Step 12a: check gate with bounded in-run CI repair ───────────────
            // The branch reviewer is an LLM — it cannot compile. CI check runs are the only
            // build/typecheck signal, so a red (or never-finishing) build blocks the merge in
            // EVERY mode, including Bootstrap. When checks FAIL with extractable findings,
            // the run repairs its own branch in place (surgical fix from compiler output,
            // recommit, re-poll) before giving up — regeneration via fresh issues never
            // converges (#95). Repos without CI have zero check runs and proceed.
            ChecksState checksState = null!;
            var ciRepairAttempt = 0;
            // D1: error signatures from the previous attempt — recurrence across attempts means the
            // surgical call-site strategy is not converging and the repair must widen to declaring types.
            IReadOnlyList<string> previousErrorSignatures = [];

            while (true)
            {
                // Poll until settled on the CURRENT head sha. poll resets each repair
                // attempt, so the zero-check registration grace applies per attempt — a
                // repair commit's checks also take seconds to register.
                for (var poll = 0; poll < MaxCheckPolls; poll++)
                {
                    checksState = await context.CallActivityAsync<ChecksState>(
                        nameof(AgentActivityFunctions.GetCheckRunsStateAsync),
                        new GetPrContextInput(agentContext.Repository, prRef.PullRequestNumber, prHeadSha));

                    if (!ShouldKeepPollingChecks(checksState, poll))
                        break;

                    await context.CreateTimer(context.CurrentUtcDateTime.Add(CheckPollInterval), CancellationToken.None);
                }

                if (!ShouldBlockOnChecks(checksState))
                    break; // green, or repo genuinely has no CI

                string? exitReason = null;
                if (!ShouldAttemptCiRepair(checksState, ciRepairAttempt))
                {
                    exitReason = checksState.FailedNames.Count > 0
                        ? $"PR checks failed: {string.Join(", ", checksState.FailedNames)} ({ciRepairAttempt} repair attempt(s) used)"
                        : "PR checks did not complete within the polling budget";
                }

                string ciFindingsRef = string.Empty;
                if (exitReason is null)
                {
                    // Never repair blind: no extractable findings is the regeneration
                    // anti-pattern this loop exists to kill.
                    ciFindingsRef = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.FetchCiFailureFindingsAsync),
                        new FetchCiFindingsInput(agentContext.RunId, agentContext.Repository, prHeadSha, ciRepairAttempt + 1));
                    if (string.IsNullOrWhiteSpace(ciFindingsRef))
                        exitReason = $"PR checks failed ({string.Join(", ", checksState.FailedNames)}) and no findings were extractable";
                }

                string branchSourceRef = string.Empty;
                if (exitReason is null)
                {
                    branchSourceRef = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.FetchExistingSourceAsync),
                        new FetchExistingSourceInput(agentContext.RunId, agentContext.Repository, branchName));
                    if (string.IsNullOrWhiteSpace(branchSourceRef))
                        exitReason = "PR checks failed and the branch source could not be bundled for repair";
                }

                if (exitReason is not null)
                {
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                        new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                            BuildChecksFailedComment(checksState, prRef.Url, ciRepairAttempt)));
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                        new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:checks-failed"));
                    await RecordWorkflowExitAsync(context, agentContext, "Stopped", exitReason);
                    return Stopped(agentContext.RunId, issue, createdAt, context);
                }

                ciRepairAttempt++;
                agentContext.Metadata["ciFindings"]     = ciFindingsRef;     // refs only — resolved agent-side
                agentContext.Metadata["existingSource"] = branchSourceRef;   // branch code, replaces any reopen bundle

                // D1 escalation: ramp-w3-booking burned six repair rounds re-patching consumer files while
                // the declaring Booking model stayed wrong — compiler errors quote the TYPE, never the
                // declaring file's path, so both the repair prompt and the minimality filter kept the model
                // out of scope forever. When error signatures recur between attempts, tell the implementer
                // to regenerate the declaring type and stop narrowing the committed set to implicated files.
                var ciFindingsText = await context.CallActivityAsync<string>(
                    nameof(AgentActivityFunctions.ResolveContextAsync), ciFindingsRef);
                var errorSignatures = AgentActivityFunctions.RepairErrorSignatures(ciFindingsText);
                var escalateRepair = AgentActivityFunctions.RepairEscalationNeeded(previousErrorSignatures, errorSignatures);
                previousErrorSignatures = errorSignatures;
                if (escalateRepair)
                    agentContext.Metadata["repairEscalation"] = "true";
                else
                    agentContext.Metadata.Remove("repairEscalation");

                var repairResult = await RunStageWithRecoveryAsync(context, agentContext, $"CI Repair (attempt {ciRepairAttempt})",
                    () => context.CallActivityAsync<AgentResult>(
                        nameof(AgentActivityFunctions.RunCodeImplementerAsync), agentContext, AgentRetryOptions));

                var repairContent = repairResult.OutputMarkdown;
                if (repairContent is null && repairResult.ContextRef is not null)
                    repairContent = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.ResolveContextAsync), repairResult.ContextRef);

                // Minimal diffs only: keep files the findings implicate (and never .github/) —
                // observed failure: repair 2 "fixed" a build by renaming namespaces (#98). Under D1
                // escalation the narrowing is skipped — it was the non-convergence mechanism.
                var repairedChanges = AgentActivityFunctions.FilterRepairChanges(
                    CodeChangeParser.Parse(repairContent), ciFindingsText, existingAcceptanceSpec: null,
                    escalated: escalateRepair);
                if (repairedChanges.Count == 0)
                {
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                        new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                            BuildChecksFailedComment(checksState, prRef.Url, ciRepairAttempt)));
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                        new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:checks-failed"));
                    await RecordWorkflowExitAsync(context, agentContext, "Stopped",
                        $"CI repair attempt {ciRepairAttempt} produced no file changes");
                    return Stopped(agentContext.RunId, issue, createdAt, context);
                }

                var repairLeaks = EmailLeakGuard.Scan(repairedChanges);
                if (repairLeaks.Count > 0)
                {
                    repairedChanges = EmailLeakGuard.Sanitize(repairedChanges).ToList();
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                        new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                            BuildEmailSanitizedComment(repairLeaks)));
                }

                var repairMsg = $"fix: repair CI failures (closes #{agentContext.IssueNumber}) [ai-sdlc]";
                string? repairLastPath = null;
                try
                {
                    foreach (var file in repairedChanges)
                    {
                        repairLastPath = file.Path;
                        await context.CallActivityAsync(
                            nameof(AgentActivityFunctions.CommitFileAsync),
                            new CommitFileInput(agentContext.Repository, file.Path, file.Content, repairMsg, branchName));
                    }
                }
                catch (TaskFailedException ex)
                {
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                        new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                            BuildCommitFailedComment(repairLastPath ?? "unknown", branchName, ex.InnerException?.Message ?? ex.Message)));
                    await RecordWorkflowExitAsync(context, agentContext, "Stopped",
                        $"CommitFileAsync failed during CI repair on '{repairLastPath}': {ex.InnerException?.Message ?? ex.Message}");
                    return Stopped(agentContext.RunId, issue, createdAt, context);
                }

                var previousSha = prHeadSha;
                prHeadSha = await context.CallActivityAsync<string>(
                    nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                    new GetHeadShaInput(agentContext.Repository, branchName));

                // The ref read is eventually consistent: w1proof1's repair commit landed at :31, the
                // re-read at :32 still returned the PRE-commit head, and the run was falsely declared
                // a no-op while the repair's CI went green 5s later. Give the ref a few beats to
                // converge before trusting "unchanged".
                for (var refRead = 0; prHeadSha == previousSha && refRead < 3; refRead++)
                {
                    await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(10), CancellationToken.None);
                    prHeadSha = await context.CallActivityAsync<string>(
                        nameof(AgentActivityFunctions.GetDefaultBranchShaActivityAsync),
                        new GetHeadShaInput(agentContext.Repository, branchName));
                }

                // No-op repair: identical content makes every commit a no-change, leaving the
                // same failed sha — re-polling it would instantly re-fail and silently burn
                // the remaining attempt (observed on 624d97a2 issue #13: two "repairs", zero
                // commits). Exit with an honest reason instead.
                if (prHeadSha == previousSha)
                {
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                        new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                            BuildChecksFailedComment(checksState, prRef.Url, ciRepairAttempt)));
                    await context.CallActivityAsync(
                        nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                        new AddLabelInput(agentContext.Repository, agentContext.IssueNumber, "ai-sdlc:checks-failed"));
                    await RecordWorkflowExitAsync(context, agentContext, "Stopped",
                        $"CI repair attempt {ciRepairAttempt} produced no effective changes (head sha unchanged)");
                    return Stopped(agentContext.RunId, issue, createdAt, context);
                }

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        BuildCiRepairAttemptComment(ciRepairAttempt, MaxCiRepairAttempts, repairedChanges.Count,
                            checksState.FailedNames, agentContext.Repository, defaultBranch, branchName, prHeadSha)));
                // → loop back to polling on the NEW sha
            }

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

        await PostBootstrapStatusMarkerAsync(context, agentContext, completed: true);

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

    // Runs an LLM-backed stage and, instead of letting retry exhaustion kill the whole
    // orchestration (v17 incident: Anthropic credit exhaustion → 400 → instance Failed,
    // recoverable only by close/reopen from scratch), parks the run: posts a comment
    // explaining the failure, audits an Awaiting state for the dashboard, and re-runs the
    // stage when a /retry comment raises RetryStage. Earlier stages are never repeated.
    private static async Task<T> RunStageWithRecoveryAsync<T>(
        TaskOrchestrationContext context, AgentContext agentContext, string stageName, Func<Task<T>> runStage)
    {
        while (true)
        {
            try
            {
                return await runStage();
            }
            catch (TaskFailedException ex)
            {
                var reason = ex.InnerException?.Message ?? ex.Message;

                await context.CallActivityAsync(
                    nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                    new PostCommentInput(agentContext.Repository, agentContext.IssueNumber,
                        BuildStageStalledComment(stageName, reason)));

                await RecordWorkflowAwaitAsync(context, agentContext,
                    "AwaitingStageRetry", $"Stage '{stageName}' failed after retries — awaiting /retry");

                using var cts   = new CancellationTokenSource();
                var retryTask   = context.WaitForExternalEvent<object?>(WorkflowEventNames.RetryStage, cts.Token);
                var timeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(StageRetryWindow), cts.Token);
                var winner      = await Task.WhenAny(retryTask, timeoutTask);
                cts.Cancel();

                if (winner == timeoutTask)
                {
                    await RecordWorkflowExitAsync(context, agentContext, "Failed",
                        $"Stage '{stageName}' abandoned — no /retry within {StageRetryWindow.TotalDays:0} days: {reason}");
                    throw;
                }
            }
        }
    }

    internal static string BuildStageStalledComment(string stageName, string reason) =>
        "## AI SDLC — Stage Failed (resumable)\n\n" +
        $"Stage **{stageName}** failed after automatic retries:\n\n" +
        $"> {reason}\n\n" +
        "The run is paused, not dead. Fix the underlying problem (e.g. API quota), then comment " +
        "`/retry` on this issue to resume. The run continues from this stage — earlier stages are " +
        "not repeated.";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GitHubIssueReference BuildIssueRef(AgentContext ctx) =>
        new(ctx.Repository, ctx.IssueNumber,
            $"https://github.com/{ctx.Repository}/issues/{ctx.IssueNumber}");

    // HTML-comment markers that external hosts (Yorrixx) sniff in issue-comment bodies to
    // flip domain state (Building → Live / Failed). Invisible in GitHub's rendered view.
    internal const string TerminalStatusMarkerCompleted = "<!-- ai-sdlc:status=completed -->";
    internal const string TerminalStatusMarkerFailed    = "<!-- ai-sdlc:status=failed -->";

    // Returns the marker to post on workflow termination, or null when no marker should fire.
    // Only Bootstrap-mode runs emit markers — Standard-mode issue feeds stay clean.
    public static string? GetTerminalStatusMarker(WorkflowMode mode, bool completed) =>
        mode == WorkflowMode.Bootstrap
            ? (completed ? TerminalStatusMarkerCompleted : TerminalStatusMarkerFailed)
            : null;

    private static async Task PostBootstrapStatusMarkerAsync(
        TaskOrchestrationContext context, AgentContext agentContext, bool completed)
    {
        var marker = GetTerminalStatusMarker(agentContext.Mode, completed);
        if (marker is null)
        {
            return;
        }

        // Emit BOTH the HTML-comment marker (fallback, scrapable) AND the typed audit event (primary
        // contract per ADR-0004 § "Terminal markers relationship"). Both fire in v1; HTML-comment
        // marker deprecation is deferred to v2 once Yorrixx is consuming the events API in production.
        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(agentContext.Repository, agentContext.IssueNumber, marker));

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.EmitBootstrapTerminalMarkerAuditAsync),
            new BootstrapTerminalMarkerAuditInput(
                agentContext.Repository,
                agentContext.IssueNumber,
                completed ? "completed" : "failed"));
    }

    // In Bootstrap mode, ALL risk outcomes (including BLOCKED) auto-promote to
    // AUTO_MERGE_ELIGIBLE. Bootstrap is unattended greenfield with no human to action a block,
    // and the AI risk assessment non-deterministically BLOCKed MVPs on aspirational compliance
    // (no published privacy policy / GDPR framework) — now addressed by the templated legal docs
    // injected into every build. The CI gate and Yorrixx verification remain the real gates, so
    // a risk BLOCK should not silently kill an autonomous build. Non-Bootstrap is unchanged.
    public static string ApplyBootstrapRiskOverride(string riskDecision, WorkflowMode mode) =>
        mode == WorkflowMode.Bootstrap
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
    // Also emits the Bootstrap terminal-status marker comment so external hosts (Yorrixx)
    // can detect the failure even when the visible terminal comment varies by exit path.
    private static async Task RecordWorkflowExitAsync(
        TaskOrchestrationContext context, AgentContext agentContext, string outcome, string reason)
    {
        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.RecordWorkflowExitAsync),
            new WorkflowExitAuditInput(agentContext.Repository, agentContext.IssueNumber, outcome, reason));
        await PostBootstrapStatusMarkerAsync(context, agentContext, completed: false);
    }

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

    private static string BuildEmailSanitizedComment(IReadOnlyList<EmailLeakGuard.Violation> leaks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Contact Emails Normalized");
        sb.AppendLine();
        sb.AppendLine("Generated output contained literal email address(es). Contact emails are substituted " +
                      "at deploy via the `" + EmailLeakGuard.ContactEmailToken + "` placeholder — a real " +
                      "address in the source would be a leak (invented or PII), so each one was rewritten " +
                      "to the placeholder before commit. The build continues.");
        sb.AppendLine();
        sb.AppendLine("**Rewritten:**");
        foreach (var leak in leaks)
            sb.AppendLine($"- `{leak.Path}` → `{leak.Email}`");
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

    // CI on template-seeded repos completes in ~2–4 minutes; 20 polls × 30 s gives a
    // 10-minute budget before unfinished checks are treated as a failure.
    internal static readonly TimeSpan CheckPollInterval = TimeSpan.FromSeconds(30);
    internal const int MaxCheckPolls = 20;

    // GitHub Actions takes seconds to register check runs for a fresh SHA, so an instant
    // zero-check read does NOT mean the repo has no CI — user-app-624d97a2 merged six
    // non-compiling builds because the gate read zero, concluded no-CI, and merged three
    // seconds before the checks appeared (and failed). Keep polling through this grace
    // before concluding the repo genuinely has no CI.
    internal const int ZeroCheckGracePolls = 4;

    internal static bool ShouldKeepPollingChecks(ChecksState state, int poll) =>
        state.Pending > 0 || (state.Total == 0 && poll < ZeroCheckGracePolls);

    // Each attempt ≈ one surgical model call + a handful of GitHub calls. Two was too shallow
    // for multi-file TypeScript error sets: v003 (fd0fc752) drove build-api red→green and
    // build-frontend down to 4 concrete TS errors but ran out of attempts mid-convergence.
    // Six gives the loop room to reconcile the diffuse intra-feature coherence sets seen once the
    // scaffold/convention/dependency layers were fixed: v007 (e3dd3b48) exhausted four attempts on
    // ~34 self-consistency errors (CS8852 init-vs-mutation, CS1061 calls to undefined methods)
    // spread across a 56-file app. The same-SHA no-op guard still exits early when a repair stops
    // making progress, so a higher cap can't spin; the stuck-Running sweep (#129) backstops a wedge.
    internal const int MaxCiRepairAttempts = 6;

    // Repair only on concrete failures: a pure pending-timeout has no findings to act on,
    // and a blind attempt is exactly the regeneration anti-pattern this loop replaces.
    // Failures coexisting with still-pending checks ARE actionable (fast-fail build,
    // slow e2e still running at the budget).
    internal static bool ShouldAttemptCiRepair(ChecksState state, int attemptsUsed) =>
        state.FailedNames.Count > 0 && attemptsUsed < MaxCiRepairAttempts;

    // Summary only — code never goes in comments (#84). Starts "## AI SDLC" so
    // ExtractReopenFindings excludes it from any future reopen-findings scrape.
    internal static string BuildCiRepairAttemptComment(
        int attempt, int maxAttempts, int fileCount, IReadOnlyList<string> failedChecks,
        string repository, string baseBranch, string branchName, string commitSha) =>
        $"## AI SDLC — CI Repair (attempt {attempt} of {maxAttempts})\n\n" +
        $"CI failed: **{string.Join("**, **", failedChecks)}**. The pipeline applied a surgical fix " +
        $"({fileCount} file(s)) and is re-running the checks.\n\n" +
        $"- **Branch:** `{branchName}`\n" +
        $"- **Commit:** `{Short(commitSha)}`\n" +
        $"- [View the changes](https://github.com/{repository}/compare/{baseBranch}...{branchName})";

    // Block when any check failed, or when checks exist but never settled within the
    // budget. Zero check runs after the registration grace (repo genuinely has no CI
    // workflows) passes — there is nothing to compile against.
    internal static bool ShouldBlockOnChecks(ChecksState state) =>
        state.FailedNames.Count > 0 || state.Pending > 0;

    internal static string BuildChecksFailedComment(ChecksState state, string prUrl, int repairAttemptsUsed = 0) =>
        "## AI SDLC — Build Checks Failed\n\n" +
        (state.FailedNames.Count > 0
            ? $"The PR's checks failed: **{string.Join("**, **", state.FailedNames)}**.\n\n"
            : "The PR's checks did not complete within the 10-minute polling budget.\n\n") +
        (repairAttemptsUsed > 0
            ? $"Automatic CI repair was attempted {repairAttemptsUsed} time(s) without converging.\n\n"
            : string.Empty) +
        $"The branch was NOT merged. Review the check logs on the [pull request]({prUrl}); " +
        "close and reopen this issue (with findings as a comment) to run the pipeline again.";

    // Code-producing stages report a summary only — the branch is the single code transport.
    // Embedding source in comments leaked it into notification emails, hit GitHub's 64KB
    // comment limit, and was the corruption surface for the v14 redaction incident (#84).
    internal static string BuildImplementationSummaryComment(
        string heading, string summary, string repository, string baseBranch,
        string branchName, string commitSha, int fileCount) =>
        $"## {heading}\n\n" +
        $"{summary}\n\n" +
        $"- **Branch:** `{branchName}`\n" +
        $"- **Commit:** `{Short(commitSha)}`\n" +
        $"- **Files changed:** {fileCount}\n" +
        $"- [View the changes](https://github.com/{repository}/compare/{baseBranch}...{branchName})\n\n" +
        "*Code lives on the branch only — file contents are never posted in comments. " +
        "The full agent output is preserved in the run's artefact store.*";

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;

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
