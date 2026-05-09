using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class OrchestratorSkeletonTests
{
    [Fact]
    public async Task AgentActivityFunctions_ShouldExecuteRegisteredPersonaActivities()
    {
        var activityFunctions = new AgentActivityFunctions(
            new AgentRunner(new IAgent[]
            {
                new ProductStrategistAgent(),
                new ProductOwnerAgent(),
                new BusinessAnalystAgent()
            }));

        var context = new AgentContext
        {
            RunId = "run-123",
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 42,
            CurrentState = "webhook-received",
            RequestedAgent = AgentNames.ProductStrategist
        };

        var strategistResult = await activityFunctions.RunProductStrategistAsync(context, CancellationToken.None);
        var ownerResult = await activityFunctions.RunProductOwnerAsync(context, CancellationToken.None);
        var analystResult = await activityFunctions.RunBusinessAnalystAsync(context, CancellationToken.None);

        Assert.Equal(AgentNames.ProductStrategist, strategistResult.AgentName);
        Assert.Equal(AgentNames.ProductOwner, ownerResult.AgentName);
        Assert.Equal(AgentNames.BusinessAnalyst, analystResult.AgentName);
    }

    [Fact]
    public void FunctionTypes_ShouldExposeExpectedSkeletonClasses()
    {
        Assert.NotNull(typeof(AiSdlcWorkflowOrchestrator));
        Assert.NotNull(typeof(GitHubWebhookFunction));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunProductStrategistAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunProductOwnerAsync)));
        Assert.NotNull(typeof(AgentActivityFunctions).GetMethod(nameof(AgentActivityFunctions.RunBusinessAnalystAsync)));
    }
}
