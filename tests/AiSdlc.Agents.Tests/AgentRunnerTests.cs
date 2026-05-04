using AiSdlc.Agents.Personas;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

public sealed class AgentRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldRunRegisteredAgentByName()
    {
        IAgentRunner runner = new AgentRunner(new IAgent[]
        {
            new ProductStrategistAgent(),
            new ProductOwnerAgent(),
            new BusinessAnalystAgent()
        });

        var request = new AgentExecutionRequest
        {
            AgentName = AgentNames.ProductStrategist,
            Context = CreateContext(AgentNames.ProductStrategist)
        };

        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Result);
        Assert.Equal(AgentNames.ProductStrategist, result.Result.AgentName);
        Assert.Equal("Completed", result.Result.Status);
        Assert.Contains("strategy", result.Result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailureForUnknownAgent()
    {
        IAgentRunner runner = new AgentRunner(new IAgent[]
        {
            new ProductStrategistAgent()
        });

        var request = new AgentExecutionRequest
        {
            AgentName = "Unknown Agent",
            Context = CreateContext("Unknown Agent")
        };

        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Result);
        Assert.Contains("No registered agent", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSupportMultipleDeterministicPersonas()
    {
        IAgentRunner runner = new AgentRunner(new IAgent[]
        {
            new ProductStrategistAgent(),
            new ProductOwnerAgent(),
            new BusinessAnalystAgent()
        });

        var ownerResult = await runner.ExecuteAsync(
            new AgentExecutionRequest
            {
                AgentName = AgentNames.ProductOwner,
                Context = CreateContext(AgentNames.ProductOwner)
            },
            CancellationToken.None);

        var analystResult = await runner.ExecuteAsync(
            new AgentExecutionRequest
            {
                AgentName = AgentNames.BusinessAnalyst,
                Context = CreateContext(AgentNames.BusinessAnalyst)
            },
            CancellationToken.None);

        Assert.Equal(AgentNames.ProductOwner, ownerResult.Result?.AgentName);
        Assert.Equal(AgentNames.BusinessAnalyst, analystResult.Result?.AgentName);
        Assert.NotNull(ownerResult.Result);
        Assert.NotNull(analystResult.Result);
        Assert.NotEmpty(ownerResult.Result.ArtefactsCreated);
        Assert.NotEmpty(analystResult.Result.FollowUpQuestions);
    }

    private static AgentContext CreateContext(string requestedAgent) =>
        new()
        {
            RunId = "run-123",
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 42,
            CurrentState = "triage",
            RequestedAgent = requestedAgent
        };
}
