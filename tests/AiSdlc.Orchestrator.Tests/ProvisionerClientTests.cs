using System.Net;
using System.Text;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.Orchestrator.Provisioning;
using AiSdlc.RepoIndex.Charter;
using Yorrixx.Provisioner.Contracts;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class ProvisionerClientTests
{
    private static ProvisionSpec SampleRequest() => new(
        AppId: "app1", BuildId: "build-app1", Env: "dev", Region: "northeurope", StackProfile: "Static",
        Capabilities: new ProvisionCapabilities(false, false, false, false, false),
        Repo: new ProvisionRepo("yorrixx-apps", "user-app-app1", "main"));

    [Fact]
    public async Task StartProvisionAsync_posts_the_request_to_provision()
    {
        var handler = new CapturingHandler(HttpStatusCode.Accepted, """{"provisionId":"p1","status":"accepted"}""");
        var client  = new ProvisionerClient(new HttpClient(handler) { BaseAddress = new Uri("https://prov.example/") });

        await client.StartProvisionAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/provision", handler.LastPath);
        Assert.Contains("app1", handler.LastBody);            // request body serialized
        Assert.Contains("build-app1", handler.LastBody);
    }

    [Fact]
    public async Task GetProvisionResultAsync_returns_the_result_when_available()
    {
        const string body = """
            {"appId":"app1","buildId":"build-app1","outcome":"provisioned","hostedUrl":"https://app1.example",
             "deploy":{"method":"oidc-federated","clientId":"c","tenantId":"t","subscriptionId":"s"}}
            """;
        var handler = new CapturingHandler(HttpStatusCode.OK, body);
        var client  = new ProvisionerClient(new HttpClient(handler) { BaseAddress = new Uri("https://prov.example/") });

        var result = await client.GetProvisionResultAsync("build-app1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("provisioned", result!.Outcome);
        Assert.Equal("https://app1.example", result.HostedUrl);
        Assert.Equal("c", result.Deploy!.ClientId);
        Assert.Equal("/provision/build-app1", handler.LastPath);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task GetProvisionResultAsync_returns_null_when_not_ready(HttpStatusCode status)
    {
        var handler = new CapturingHandler(status, "");
        var client  = new ProvisionerClient(new HttpClient(handler) { BaseAddress = new Uri("https://prov.example/") });

        Assert.Null(await client.GetProvisionResultAsync("build-app1", CancellationToken.None));
    }

    [Fact]
    public void Capabilities_From_profile_maps_every_axis()
    {
        var profile = new CapabilityProfile { Api = true, Database = true, Auth = true, Payments = true, Email = false, AIApi = false };
        var caps = profile.ToProvisionCapabilities();

        Assert.True(caps.Auth);
        Assert.True(caps.Database);
        Assert.True(caps.Payments);
        Assert.False(caps.Email);
        Assert.False(caps.AiApi);
    }

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastPath   = request.RequestUri!.AbsolutePath;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
