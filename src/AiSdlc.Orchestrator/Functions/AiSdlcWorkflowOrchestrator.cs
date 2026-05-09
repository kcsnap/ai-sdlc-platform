using AiSdlc.Agents;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace AiSdlc.Orchestrator.Functions;

public static class AiSdlcWorkflowOrchestrator
{
    [Function(nameof(AiSdlcWorkflowOrchestrator))]
    public static async Task<WorkflowRun> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var agentContext = context.GetInput<AgentContext>()
            ?? throw new InvalidOperationException("Workflow input must include an AgentContext payload.");

        var strategistResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunProductStrategistAsync),
            agentContext);

        var ownerResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunProductOwnerAsync),
            agentContext);

        var analystResult = await context.CallActivityAsync<AgentResult>(
            nameof(AgentActivityFunctions.RunBusinessAnalystAsync),
            agentContext);

        var now = DateTimeOffset.UtcNow;

        return new WorkflowRun
        {
            RunId = agentContext.RunId,
            Repository = agentContext.Repository,
            Issue = new GitHubIssueReference(
                agentContext.Repository,
                agentContext.IssueNumber,
                $"https://github.com/{agentContext.Repository}/issues/{agentContext.IssueNumber}"),
            Status = WorkflowRunStatus.AwaitingHumanReview,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RiskLevel = RiskLevel.Unknown,
            RiskDecision = RiskDecision.Unknown.ToString(),
            Artefacts = MapArtefacts(strategistResult, ownerResult, analystResult)
        };
    }

    private static IReadOnlyList<ArtefactReference> MapArtefacts(params AgentResult[] results) =>
        results
            .SelectMany(result => result.ArtefactsCreated.Select(artefact =>
                new ArtefactReference(artefact, "generated", artefact)))
            .ToArray();
}
