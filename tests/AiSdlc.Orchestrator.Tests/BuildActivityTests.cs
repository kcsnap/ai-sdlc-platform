using AiSdlc.GitHub;
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

    private static CheckRunResult Check(string status, string conclusion) =>
        new() { Name = "deploy", Status = status, Conclusion = conclusion };

    [Fact]
    public void SummarizeDeploy_classifies_check_state()
    {
        Assert.Equal("none", BuildActivityFunctions.SummarizeDeploy([]));
        Assert.Equal("running", BuildActivityFunctions.SummarizeDeploy([Check("in_progress", "")]));
        Assert.Equal("success", BuildActivityFunctions.SummarizeDeploy([Check("completed", "success")]));
        Assert.Equal("failed", BuildActivityFunctions.SummarizeDeploy([Check("completed", "failure")]));
        Assert.Equal("failed", BuildActivityFunctions.SummarizeDeploy(
            [Check("completed", "success"), Check("completed", "failure")]));
    }

    [Fact]
    public void AssembleVerification_passes_when_deploy_green_and_serving()
    {
        var staticResult = BuildActivityFunctions.AssembleVerification("success", 200, isStatic: true);
        Assert.Equal("passed", staticResult.Outcome);
        Assert.Contains(staticResult.Checks, c => c.CheckId == "api-health" && c.Status == "skipped");

        var fullStack = BuildActivityFunctions.AssembleVerification("success", 200, isStatic: false);
        Assert.Equal("passed", fullStack.Outcome);
        Assert.Contains(fullStack.Checks, c => c.CheckId == "api-health" && c.Status == "pass");
    }

    [Theory]
    [InlineData("failed", 200)]   // deploy red
    [InlineData("success", 500)]  // serves error
    [InlineData("success", 0)]    // probe failed
    public void AssembleVerification_fails_on_red_deploy_or_bad_serve(string deploy, int serves)
    {
        Assert.Equal("failed", BuildActivityFunctions.AssembleVerification(deploy, serves, isStatic: true).Outcome);
    }

    [Theory]
    [InlineData("https://api.example/v1/admin",  "app1", "status",       "https://api.example/v1/admin/apps/app1/status")]
    [InlineData("https://api.example/v1/admin/", "app1", "runtime",      "https://api.example/v1/admin/apps/app1/runtime")]  // trailing slash trimmed
    [InlineData("https://api.example/v1/admin",  "app1", "verification", "https://api.example/v1/admin/apps/app1/verification")]
    public void CallbackUrl_builds_the_apps_path(string baseUrl, string appId, string kind, string expected)
    {
        Assert.Equal(expected, BuildActivityFunctions.CallbackUrl(baseUrl, appId, kind));
    }
}
