namespace AiSdlc.Agents;

public sealed class AgentRunner : IAgentRunner
{
    private readonly IReadOnlyDictionary<string, IAgent> _agentsByName;

    public AgentRunner(IEnumerable<IAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        _agentsByName = agents.ToDictionary(agent => agent.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_agentsByName.TryGetValue(request.AgentName, out var agent))
        {
            return new AgentExecutionResult
            {
                Succeeded = false,
                AgentName = request.AgentName,
                ErrorMessage = $"No registered agent was found for '{request.AgentName}'."
            };
        }

        var result = await agent.ExecuteAsync(request, cancellationToken);

        return new AgentExecutionResult
        {
            Succeeded = true,
            AgentName = request.AgentName,
            Result = result
        };
    }
}
