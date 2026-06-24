using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class BuildActivityTests
{
    [Theory]
    [InlineData("Static",        "yorrixx-apps/ai-sdlc-static-template")]
    [InlineData("static",        "yorrixx-apps/ai-sdlc-static-template")]   // case-insensitive
    [InlineData("FullStack",     "yorrixx-apps/ai-sdlc-react-dotnet-template")]
    [InlineData("anything-else", "yorrixx-apps/ai-sdlc-react-dotnet-template")]  // default to FullStack
    public void ResolveTemplateRepo_selects_static_only_for_static(string profile, string expected)
    {
        var repo = BuildActivityFunctions.ResolveTemplateRepo(
            profile, "yorrixx-apps",
            BuildActivityFunctions.DefaultStaticTemplate,
            BuildActivityFunctions.DefaultFullStackTemplate);

        Assert.Equal(expected, repo);
    }

    [Fact]
    public void RepoName_is_prefixed_with_user_app()
    {
        Assert.Equal("user-app-dd0e9574", BuildActivityFunctions.RepoName("dd0e9574"));
    }
}
