using AiSdlc.Shared;

namespace AiSdlc.Agents;

public interface IAgent
{
    string Name { get; }
    Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken);
}
