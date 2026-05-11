using System.Text;
using AiSdlc.Agents;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace AiSdlc.Orchestrator.Functions;

public static class AiSdlcWorkflowOrchestrator
{
    private const int MaxBriefAttempts = 3;
    private static readonly TimeSpan BriefApprovalTimeout = TimeSpan.FromDays(7);

    [Function(nameof(AiSdlcWorkflowOrchestrator))]
    public static async Task<WorkflowRun> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var agentContext = context.GetInput<AgentContext>()
            ?? throw new InvalidOperationException("Workflow input must include an AgentContext payload.");

        var issue = new GitHubIssueReference(
            agentContext.Repository,
            agentContext.IssueNumber,
            $"https://github.com/{agentContext.Repository}/issues/{agentContext.IssueNumber}");

        // Use context.CurrentUtcDateTime for determinism during replay
        var createdAt = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

        // ── Step 1: ProductStrategist reviews value & feasibility ──────────────
        var strategistResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunProductStrategistAsync), agentContext);

        // Pass strategist output to subsequent agents via metadata
        agentContext.Metadata["strategistOutput"] = strategistResult.OutputMarkdown ?? strategistResult.Summary;

        // ── Step 2: ProductOwner writes the brief ──────────────────────────────
        // Up to MaxBriefAttempts rounds of revisions before giving up.
        AgentResult ownerResult = strategistResult; // will be overwritten below
        var briefApproved = false;

        for (var attempt = 0; attempt < MaxBriefAttempts && !briefApproved; attempt++)
        {
            ownerResult = await context.CallActivityAsync<AgentResult>(
                nameof(AgentActivityFunctions.RunProductOwnerAsync), agentContext);

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.PostGitHubCommentAsync),
                new PostCommentInput(
                    agentContext.Repository,
                    agentContext.IssueNumber,
                    BuildBriefComment(ownerResult, attempt)));

            await context.CallActivityAsync(
                nameof(AgentActivityFunctions.AddGitHubLabelAsync),
                new AddLabelInput(
                    agentContext.Repository,
                    agentContext.IssueNumber,
                    "ai-sdlc:awaiting-brief-approval"));

            // ── Wait for human response ────────────────────────────────────────
            using var cts = new CancellationTokenSource();
            var approveTask  = context.WaitForExternalEvent<object?>(WorkflowEventNames.ApproveBrief,  cts.Token);
            var changesTask  = context.WaitForExternalEvent<object?>(WorkflowEventNames.RequestChanges, cts.Token);
            var timeoutTask  = context.CreateTimer(
                context.CurrentUtcDateTime.Add(BriefApprovalTimeout), cts.Token);

            var winner = await Task.WhenAny(approveTask, changesTask, timeoutTask);
            cts.Cancel(); // cancel timer if an event arrived first

            if (winner == approveTask)
            {
                briefApproved = true;
                break;
            }

            if (winner == timeoutTask)
                return Stopped(agentContext.RunId, issue, createdAt, context);

            // RequestChanges received — loop if attempts remain
            if (attempt == MaxBriefAttempts - 1)
                return Stopped(agentContext.RunId, issue, createdAt, context);
        }

        // Pass approved brief to Business Analyst
        agentContext.Metadata["ownerBrief"] = ownerResult.OutputMarkdown ?? ownerResult.Summary;

        // ── Step 3: Brief approved — Business Analyst analyses impact ──────────
        var analystResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunBusinessAnalystAsync), agentContext);

        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.PostGitHubCommentAsync),
            new PostCommentInput(
                agentContext.Repository,
                agentContext.IssueNumber,
                BuildAnalysisComment(analystResult)));

        var updatedAt = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

        return new WorkflowRun
        {
            RunId        = agentContext.RunId,
            Repository   = agentContext.Repository,
            Issue        = issue,
            Status       = WorkflowRunStatus.AwaitingHumanReview,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt,
            RiskLevel    = RiskLevel.Unknown,
            RiskDecision = RiskDecision.Unknown.ToString(),
            Artefacts    = MapArtefacts(strategistResult, ownerResult, analystResult)
        };
    }

    private static WorkflowRun Stopped(
        string runId, GitHubIssueReference issue, DateTimeOffset createdAt, TaskOrchestrationContext context) =>
        new()
        {
            RunId        = runId,
            Repository   = issue.Repository,
            Issue        = issue,
            Status       = WorkflowRunStatus.Stopped,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero),
            RiskLevel    = RiskLevel.Unknown,
            RiskDecision = RiskDecision.Unknown.ToString()
        };

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
        sb.AppendLine(!string.IsNullOrWhiteSpace(result.OutputMarkdown) ? result.OutputMarkdown : result.Summary);

        if (result.FollowUpQuestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Questions for clarification");
            foreach (var q in result.FollowUpQuestions)
                sb.AppendLine($"- {q}");
        }

        if (result.BlockingIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Blocking issues");
            foreach (var b in result.BlockingIssues)
                sb.AppendLine($"- {b}");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Reply `/approve-brief` to proceed or `/request-changes` with your feedback.");

        return sb.ToString();
    }

    private static string BuildAnalysisComment(AgentResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## AI SDLC — Business Analysis");
        sb.AppendLine();
        sb.AppendLine(!string.IsNullOrWhiteSpace(result.OutputMarkdown) ? result.OutputMarkdown : result.Summary);

        if (result.BlockingIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Blocking issues (require resolution before proceeding)");
            foreach (var b in result.BlockingIssues)
                sb.AppendLine($"- {b}");
        }

        return sb.ToString();
    }

    private static IReadOnlyList<ArtefactReference> MapArtefacts(params AgentResult[] results) =>
        results
            .SelectMany(r => r.ArtefactsCreated.Select(a => new ArtefactReference(a, "generated", a)))
            .ToArray();
}
