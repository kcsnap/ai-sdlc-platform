using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ProductStrategistAgent : IAgent
{
    public string Name => AgentNames.ProductStrategist;

    public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AgentResult
        {
            AgentName = Name,
            Status = "Completed",
            Summary = $"Drafted product strategy guidance for issue #{request.Context.IssueNumber} in {request.Context.Repository}.",
            Decision = "Recommend scoping the first delivery slice before implementation.",
            ArtefactsCreated = new() { "strategy-brief.md" },
            FollowUpQuestions = new() { "What is the smallest demonstrable user outcome for the next slice?" }
        });
    }
}
