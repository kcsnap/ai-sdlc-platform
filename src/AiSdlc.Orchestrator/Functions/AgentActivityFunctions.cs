using AiSdlc.Agents;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;

namespace AiSdlc.Orchestrator.Functions;

public sealed class AgentActivityFunctions
{
    private readonly IAgentRunner _agentRunner;

    public AgentActivityFunctions(IAgentRunner agentRunner)
    {
        _agentRunner = agentRunner;
    }

    [Function(nameof(RunProductStrategistAsync))]
    public Task<AgentResult> RunProductStrategistAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ProductStrategist, context, cancellationToken);

    [Function(nameof(RunProductOwnerAsync))]
    public Task<AgentResult> RunProductOwnerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ProductOwner, context, cancellationToken);

    [Function(nameof(RunBusinessAnalystAsync))]
    public Task<AgentResult> RunBusinessAnalystAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.BusinessAnalyst, context, cancellationToken);

    private async Task<AgentResult> ExecuteAsync(string agentName, AgentContext context, CancellationToken cancellationToken)
    {
        var executionResult = await _agentRunner.ExecuteAsync(
            new AgentExecutionRequest
            {
                AgentName = agentName,
                Context = context
            },
            cancellationToken);

        if (!executionResult.Succeeded || executionResult.Result is null)
        {
            throw new InvalidOperationException(executionResult.ErrorMessage ?? $"Agent execution failed for '{agentName}'.");
        }

        return executionResult.Result;
    }
}
