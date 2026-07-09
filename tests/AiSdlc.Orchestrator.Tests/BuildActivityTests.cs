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

    // G6 P2: repos must be user-app-{appId8} (first 8 of the hyphen-stripped, lowercased appId — the
    // provisioner's ResourceNames derivation) so the federated-credential subject matches the repo.
    [Theory]
    [InlineData("c50cb42440c2462a93a9777a800cc44d",     "user-app-c50cb424")] // full 32-char production id
    [InlineData("b5acec87-1111-2222-3333-444455556666", "user-app-b5acec87")] // hyphenated GUID form
    [InlineData("C50CB42440C2462A93A9777A800CC44D",     "user-app-c50cb424")] // lowercased
    [InlineData("g5x0702d-test",                        "user-app-g5x0702d")] // hyphen stripped before slicing
    [InlineData("ab-12",                                "user-app-ab120000")] // short ids padded with '0'
    public void RepoName_uses_appId8_matching_the_federated_credential_subject(string appId, string expected)
    {
        Assert.Equal(expected, BuildActivityFunctions.RepoName(appId));
    }

    // G6 P4: lost callbacks must be visible in the run output.
    [Fact]
    public void CallbackSuffix_is_empty_when_all_callbacks_delivered_and_counts_failures()
    {
        Assert.Equal(string.Empty, NewAppBuildOrchestrator.CallbackSuffix(0));
        Assert.Equal(":callbacksFailed=3", NewAppBuildOrchestrator.CallbackSuffix(3));
    }

    // F3(b): the scaffold gate — flip #4 went LIVE with raw template content and green checks.
    [Theory]
    [InlineData(null,                                          "TaskFlow", true)]  // unfetchable ⇒ scaffold
    [InlineData("",                                            "TaskFlow", true)]
    [InlineData("<html><title>App</title>TaskFlow</html>",     "TaskFlow", true)]  // template scaffold title
    [InlineData("<html><h1>{{HERO_TITLE}}</h1>TaskFlow</html>", "TaskFlow", true)] // unfilled tokens
    [InlineData("<html><title>Something</title></html>",       "TaskFlow", true)]  // app name absent
    [InlineData("<html><title>TaskFlow — tasks</title></html>", "TaskFlow", false)]
    [InlineData("<html><h1>taskflow</h1></html>",              "TaskFlow", false)] // case-insensitive
    public void ContentLooksScaffold_detects_template_output(string? html, string appName, bool expected)
    {
        Assert.Equal(expected, BuildActivityFunctions.ContentLooksScaffold(html, appName));
    }

    [Fact]
    public void AssembleVerification_fails_on_scaffold_content_even_when_deploy_and_probe_pass()
    {
        var v = BuildActivityFunctions.AssembleVerification(
            "success", 200, isStatic: true, pageHtml: "<title>App</title>", appName: "TaskFlow");

        Assert.Equal("failed", v.Outcome);
        var content = Assert.Single(v.Checks, c => c.CheckId == "content-not-scaffold");
        Assert.Equal("fail", content.Status);
    }

    [Fact]
    public void AssembleVerification_passes_when_content_is_charter_derived()
    {
        var v = BuildActivityFunctions.AssembleVerification(
            "success", 200, isStatic: true, pageHtml: "<title>TaskFlow</title><h1>TaskFlow</h1>", appName: "TaskFlow");

        Assert.Equal("passed", v.Outcome);
        Assert.Equal(4, v.Checks.Count);
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
