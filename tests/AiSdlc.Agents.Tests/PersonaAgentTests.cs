using AiSdlc.Agents.Personas;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

/// <summary>
/// Smoke tests for all persona agents: verify they complete without error
/// and return non-empty output when given minimal context.
/// </summary>
public sealed class PersonaAgentTests
{
    private static IModelProvider FakeModel() => new FakeModelProvider(new ModelProviderOptions
    {
        ProviderName     = "Fake",
        ModelName        = "fake-model",
        DefaultMaxTokens = 1024
    });

    private static AgentExecutionRequest MakeRequest(string agentName) => new()
    {
        AgentName = agentName,
        Context   = new AgentContext
        {
            RunId          = "run-test",
            Repository     = "owner/repo",
            IssueNumber    = 1,
            CurrentState   = "Started",
            RequestedAgent = agentName,
            Metadata       =
            {
                ["issueTitle"]     = "Add search to product list",
                ["issueBody"]      = "Users need search and filter on /products page.",
                ["issueAuthor"]    = "testuser",
                ["ownerBrief"]     = "Brief: add search bar and category filter.",
                ["analystOutput"]  = "BA analysis: affects ProductListPage and GET /api/products.",
                ["architectOutput"] = "Architecture: use React useState, pure filter function.",
            }
        }
    };

    [Fact]
    public async Task ArchitectAgent_Completes()
    {
        var result = await new ArchitectAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.Architect), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
        Assert.NotEmpty(result.ArtefactsCreated);
    }

    [Fact]
    public async Task SecurityPrivacyReviewerAgent_Completes()
    {
        var result = await new SecurityPrivacyReviewerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.SecurityPrivacyReviewer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task UxAccessibilityReviewerAgent_Completes()
    {
        var result = await new UxAccessibilityReviewerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.UxAccessibilityReviewer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task QaTestEngineerAgent_Completes()
    {
        var result = await new QaTestEngineerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.QaTestEngineer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task SeniorCoderAgent_Completes()
    {
        var result = await new SeniorCoderAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.SeniorCoder), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task DevOpsPlatformEngineerAgent_Completes()
    {
        var result = await new DevOpsPlatformEngineerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.DevOpsPlatformEngineer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task ComplianceLegalReviewerAgent_Completes()
    {
        var result = await new ComplianceLegalReviewerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.ComplianceLegalReviewer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task ContentSeoReviewerAgent_Completes()
    {
        var result = await new ContentSeoReviewerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.ContentSeoReviewer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task DataAnalyticsReviewerAgent_Completes()
    {
        var result = await new DataAnalyticsReviewerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.DataAnalyticsReviewer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }

    [Fact]
    public async Task RiskAssessorAgent_Completes()
    {
        var result = await new RiskAssessorAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.RiskAssessor), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
        Assert.NotNull(result.Decision);
    }

    [Fact]
    public async Task ReleaseManagerAgent_Completes()
    {
        var result = await new ReleaseManagerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.ReleaseManager), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
        Assert.NotEmpty(result.ArtefactsCreated);
    }

    [Fact]
    public async Task CodeImplementerAgent_Completes()
    {
        var result = await new CodeImplementerAgent(FakeModel())
            .ExecuteAsync(MakeRequest(AgentNames.CodeImplementer), CancellationToken.None);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
    }
}
