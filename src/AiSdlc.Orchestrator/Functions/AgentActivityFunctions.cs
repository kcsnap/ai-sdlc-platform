using AiSdlc.Agents;
using AiSdlc.GitHub;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

public sealed class AgentActivityFunctions
{
    private readonly IAgentRunner _agentRunner;
    private readonly IGitHubService _gitHub;
    private readonly ILogger<AgentActivityFunctions> _logger;

    public AgentActivityFunctions(
        IAgentRunner agentRunner,
        IGitHubService gitHub,
        ILogger<AgentActivityFunctions> logger)
    {
        _agentRunner = agentRunner;
        _gitHub      = gitHub;
        _logger      = logger;
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

    [Function(nameof(PostGitHubCommentAsync))]
    public async Task PostGitHubCommentAsync([ActivityTrigger] PostCommentInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Posting comment to {Repository}#{Issue}", input.Repository, input.IssueNumber);
        await _gitHub.AddIssueCommentAsync(input.Repository, input.IssueNumber, input.Markdown, cancellationToken);
    }

    [Function(nameof(AddGitHubLabelAsync))]
    public async Task AddGitHubLabelAsync([ActivityTrigger] AddLabelInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding label '{Label}' to {Repository}#{Issue}", input.Label, input.Repository, input.IssueOrPrNumber);
        await _gitHub.AddLabelsAsync(input.Repository, input.IssueOrPrNumber, [input.Label], cancellationToken);
    }

    private async Task<AgentResult> ExecuteAsync(string agentName, AgentContext context, CancellationToken cancellationToken)
    {
        var executionResult = await _agentRunner.ExecuteAsync(
            new AgentExecutionRequest { AgentName = agentName, Context = context },
            cancellationToken);

        if (!executionResult.Succeeded || executionResult.Result is null)
            throw new InvalidOperationException(executionResult.ErrorMessage ?? $"Agent execution failed for '{agentName}'.");

        return executionResult.Result;
    }
}
