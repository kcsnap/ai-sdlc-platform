using AiSdlc.Orchestrator.Functions;
using AiSdlc.Orchestrator.Imagery;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class ImageryPlanTests
{
    [Fact]
    public void Parses_a_yes_decision_with_queries()
    {
        var (use, queries) = AgentActivityFunctions.ParseImageryPlan(
            """{"useImagery": true, "queries": ["woman relaxing with coffee at home", "roasted beans close-up"], "rationale": "warm lifestyle"}""");

        Assert.True(use);
        Assert.Equal(2, queries.Count);
        Assert.Equal("woman relaxing with coffee at home", queries[0]);
    }

    [Fact]
    public void Parses_a_yes_decision_wrapped_in_prose()
    {
        var (use, queries) = AgentActivityFunctions.ParseImageryPlan(
            """Sure — here is the plan: {"useImagery": true, "queries": ["bright natural smile"]} done.""");

        Assert.True(use);
        Assert.Single(queries);
    }

    [Theory]
    [InlineData("""{"useImagery": false, "queries": []}""")]   // explicit no
    [InlineData("""{"useImagery": true, "queries": []}""")]    // yes but no queries → nothing to fetch
    [InlineData("""{"useImagery": "true", "queries": ["x"]}""")] // non-bool → not a yes
    [InlineData("not json")]
    [InlineData("")]
    [InlineData(null)]
    public void Defaults_to_no_imagery_when_absent_or_unusable(string? responseText)
    {
        var (use, queries) = AgentActivityFunctions.ParseImageryPlan(responseText);
        Assert.False(use);
        Assert.Empty(queries);
    }

    [Fact]
    public async Task NoOp_image_source_returns_null()
    {
        var manifest = await new NoOpImageSource().BuildManifestAsync(["anything"], CancellationToken.None);
        Assert.Null(manifest);
    }
}
