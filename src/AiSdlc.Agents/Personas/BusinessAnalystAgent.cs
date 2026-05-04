using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class BusinessAnalystAgent : IAgent
{
    public string Name => AgentNames.BusinessAnalyst;

    public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AgentResult
        {
            AgentName = Name,
            Status = "Completed",
            Summary = $"Captured analysis notes for {request.Context.RequestedAgent} on issue #{request.Context.IssueNumber}.",
            Decision = "Clarify acceptance criteria before enabling autonomous continuation.",
            ArtefactsCreated = new() { "analysis-notes.md" },
            FollowUpQuestions = new() { "What acceptance criteria define a successful run outcome?" }
        });
    }
}
