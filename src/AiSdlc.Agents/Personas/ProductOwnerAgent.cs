using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ProductOwnerAgent : IAgent
{
    public string Name => AgentNames.ProductOwner;

    public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AgentResult
        {
            AgentName = Name,
            Status = "Completed",
            Summary = $"Refined backlog priorities for workflow run {request.Context.RunId}.",
            Decision = "Proceed with the highest-value deterministic foundation work first.",
            ArtefactsCreated = new() { "backlog-priorities.md" },
            FollowUpQuestions = new() { "Which dependency must be delivered before orchestration logic?" }
        });
    }
}
