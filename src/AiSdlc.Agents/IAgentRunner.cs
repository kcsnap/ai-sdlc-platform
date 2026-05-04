namespace AiSdlc.Agents;

public interface IAgentRunner
{
    Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken);
}
