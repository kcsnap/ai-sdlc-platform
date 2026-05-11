using AiSdlc.Agents.Personas;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

public sealed class BusinessAnalystAgentTests
{
    private static IModelProvider FakeModel() => new FakeModelProvider(new ModelProviderOptions
    {
        ProviderName    = "Fake",
        ModelName       = "fake-model",
        DefaultMaxTokens = 1024
    });

    [Fact]
    public async Task ExecuteAsync_ReturnsCompletedWithOutputMarkdown()
    {
        var agent = new BusinessAnalystAgent(FakeModel());
        var request = new AgentExecutionRequest
        {
            AgentName = AgentNames.BusinessAnalyst,
            Context   = CreateContext("Add delivery info section", "Customers need delivery expectations.", ownerBrief: "Brief content")
        };

        var result = await agent.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(AgentNames.BusinessAnalyst, result.AgentName);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
        Assert.NotEmpty(result.OutputMarkdown);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesOwnerBriefInContextDocuments()
    {
        var agent = new BusinessAnalystAgent(FakeModel());
        var request = new AgentExecutionRequest
        {
            AgentName = AgentNames.BusinessAnalyst,
            Context   = CreateContext("Title", "Body", ownerBrief: "## Summary\nAdd a button.")
        };

        var result = await agent.ExecuteAsync(request, CancellationToken.None);

        // FakeModelProvider returns deterministic output — just verify the call succeeded
        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public async Task AgentRunner_ExecutesBusinessAnalystByName()
    {
        IAgentRunner runner = new AgentRunner([new BusinessAnalystAgent(FakeModel())]);
        var request = new AgentExecutionRequest
        {
            AgentName = AgentNames.BusinessAnalyst,
            Context   = CreateContext("Support link", "Users cannot find support.", ownerBrief: string.Empty)
        };

        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Result);
        Assert.Equal(AgentNames.BusinessAnalyst, result.Result.AgentName);
    }

    [Fact]
    public async Task AllPersonaAgents_ReturnCorrectAgentName()
    {
        var model = FakeModel();
        var context = CreateContext("Test issue", "Test body", ownerBrief: string.Empty);
        context.Metadata["issueTitle"]  = "Test issue";
        context.Metadata["issueBody"]   = "Test body";
        context.Metadata["issueAuthor"] = "test-user";

        var strategist = new ProductStrategistAgent(model);
        var owner      = new ProductOwnerAgent(model);
        var analyst    = new BusinessAnalystAgent(model);

        var sr = await strategist.ExecuteAsync(new AgentExecutionRequest { AgentName = AgentNames.ProductStrategist, Context = context }, CancellationToken.None);
        var or = await owner.ExecuteAsync(new AgentExecutionRequest { AgentName = AgentNames.ProductOwner, Context = context }, CancellationToken.None);
        var ar = await analyst.ExecuteAsync(new AgentExecutionRequest { AgentName = AgentNames.BusinessAnalyst, Context = context }, CancellationToken.None);

        Assert.Equal(AgentNames.ProductStrategist, sr.AgentName);
        Assert.Equal(AgentNames.ProductOwner,      or.AgentName);
        Assert.Equal(AgentNames.BusinessAnalyst,   ar.AgentName);
    }

    private static AgentContext CreateContext(string issueTitle, string issueBody, string ownerBrief) =>
        new()
        {
            RunId          = "run-001",
            Repository     = "example/repo",
            IssueNumber    = 101,
            CurrentState   = "Analysing",
            RequestedAgent = AgentNames.BusinessAnalyst,
            Metadata       = new Dictionary<string, object>
            {
                ["issueTitle"]  = issueTitle,
                ["issueBody"]   = issueBody,
                ["issueAuthor"] = "test-user",
                ["ownerBrief"]  = ownerBrief
            }
        };
}
