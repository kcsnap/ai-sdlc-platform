using AiSdlc.Orchestrator.Builds;
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

    [Fact]
    public void DeployVariables_maps_oidc_and_clerk()
    {
        var deploy = new ProvisionDeploy { ClientId = "c", TenantId = "t", SubscriptionId = "s" };
        var vars = BuildActivityFunctions.DeployVariables(deploy, "pk_test");

        Assert.Equal(4, vars.Count);
        Assert.Contains(("AZURE_CLIENT_ID", "c"), vars);
        Assert.Contains(("AZURE_TENANT_ID", "t"), vars);
        Assert.Contains(("AZURE_SUBSCRIPTION_ID", "s"), vars);
        Assert.Contains(("CLERK_PUBLISHABLE_KEY", "pk_test"), vars);
    }

    [Fact]
    public void DeployVariables_skips_absent_values()
    {
        // No Clerk (Static app) → only the three Azure OIDC vars.
        var deploy = new ProvisionDeploy { ClientId = "c", TenantId = "t", SubscriptionId = "s" };
        Assert.Equal(3, BuildActivityFunctions.DeployVariables(deploy, clerkPublishableKey: null).Count);

        // No deploy + no clerk → nothing to set.
        Assert.Empty(BuildActivityFunctions.DeployVariables(deploy: null, clerkPublishableKey: null));
    }
}
