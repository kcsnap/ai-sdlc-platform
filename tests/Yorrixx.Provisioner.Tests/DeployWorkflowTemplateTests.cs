using Yorrixx.DeployTemplate;
using Xunit;

namespace Yorrixx.Provisioner.Tests;

/// <summary>
/// D13 (TDD red-first): the FullStack deploy must carry a render-smoke job that asserts the deployed
/// frontend actually TALKS to its API — booking shipped a frontend whose every API call went to
/// "/function%20Rh(n){…}/api/bookings", the SPA fallback answered 200, and every existing gate stayed
/// green because frontend and API were only ever verified in isolation.
/// </summary>
public sealed class DeployWorkflowTemplateTests
{
    private static string RenderFullStack() => DeployWorkflowTemplate.Render(
        repoOwner: "yorrixx-apps", repoName: "user-app-d13test", tenantId: "tid", clientId: "cid",
        subscriptionId: "sid", resourceGroup: "rg-x",
        frontendWebAppName: "app-web-d13", apiFunctionAppName: "func-d13api", defaultBranch: "main",
        clerkPublishableKey: "pk_test_x", isStatic: false,
        apiBaseUrl: "https://func-d13api.azurewebsites.net");

    private static string RenderStatic() => DeployWorkflowTemplate.Render(
        repoOwner: "yorrixx-apps", repoName: "user-app-d13test", tenantId: "tid", clientId: "cid",
        subscriptionId: "sid", resourceGroup: "rg-x",
        frontendWebAppName: "std13", apiFunctionAppName: "", defaultBranch: "main",
        isStatic: true);

    [Fact]
    public void FullStack_deploy_carries_a_render_smoke_that_asserts_an_api_origin_request()
    {
        var yaml = RenderFullStack();

        Assert.Contains("render-smoke", yaml);                             // the job exists
        Assert.Contains("needs: [deploy-frontend, deploy-api]", yaml);     // runs after BOTH halves
        Assert.Contains("https://func-d13api.azurewebsites.net", yaml);    // knows the API origin
        Assert.Contains("no frontend request reached the API host", yaml); // the D13 assertion's failure text
        Assert.Contains("page.on('request'", yaml);                        // network capture, not probing
    }

    [Fact]
    public void Static_deploy_render_smoke_has_no_api_origin_assertion()
    {
        var yaml = RenderStatic();

        Assert.Contains("Render smoke", yaml);                                  // D8 smoke still present
        Assert.DoesNotContain("no frontend request reached the API host", yaml); // static apps have no API
    }
}
