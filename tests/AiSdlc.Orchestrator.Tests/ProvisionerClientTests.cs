using System.Net;
using System.Text;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.Orchestrator.Provisioning;
using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;
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

    // The GET serves the provisioner's ProvisionStatus envelope — the result is NESTED under "result".
    // (This test previously pinned a flat ProvisionResult body, which the provisioner never emits; the
    // client deserialized Outcome as null and the poll fallback could only ever report failure.)
    [Fact]
    public async Task GetProvisionResultAsync_unwraps_the_status_envelope()
    {
        const string body = """
            {"status":"provisioned","result":
             {"appId":"app1","buildId":"build-app1","outcome":"provisioned","hostedUrl":"https://app1.example",
              "deploy":{"method":"oidc-federated","clientId":"c","tenantId":"t","subscriptionId":"s"}}}
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

    [Fact]
    public async Task GetProvisionResultAsync_returns_null_while_accepted()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"status":"accepted","result":null}""");
        var client  = new ProvisionerClient(new HttpClient(handler) { BaseAddress = new Uri("https://prov.example/") });

        Assert.Null(await client.GetProvisionResultAsync("build-app1", CancellationToken.None));
    }

    // Cold-start hardening: a transport failure on Call 1 gets ONE retry, guarded by a status check so a
    // POST whose 202 was lost in transit is never re-sent (re-POST double-enqueues the provision worker).
    [Fact]
    public async Task StartProvisionAsync_retries_once_when_the_post_fails_and_the_build_is_unknown()
    {
        var handler = new ScriptedHandler(
            _ => throw new TaskCanceledException("timeout"),                        // POST #1: cold start
            _ => Respond(HttpStatusCode.NotFound, ""),                              // GET: no record → safe
            _ => Respond(HttpStatusCode.Accepted, """{"status":"accepted"}"""));    // POST #2: lands

        var client = new ProvisionerClient(new HttpClient(handler) { BaseAddress = new Uri("https://prov.example/") });
        await client.StartProvisionAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(["POST /provision", "GET /provision/build-app1", "POST /provision"], handler.Calls);
    }

    [Fact]
    public async Task StartProvisionAsync_does_not_repost_when_the_provisioner_already_accepted_the_build()
    {
        var handler = new ScriptedHandler(
            _ => throw new HttpRequestException("connection reset"),                 // POST #1: reply lost
            _ => Respond(HttpStatusCode.OK, """{"status":"accepted","result":null}""")); // GET: it landed

        var client = new ProvisionerClient(new HttpClient(handler) { BaseAddress = new Uri("https://prov.example/") });
        await client.StartProvisionAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(["POST /provision", "GET /provision/build-app1"], handler.Calls);
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

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// One canned reaction per expected call, in order; records "METHOD /path" for each call made.
    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] script) : HttpMessageHandler
    {
        private int _next;
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            Assert.InRange(_next, 0, script.Length - 1); // more calls than scripted = the test is wrong
            return Task.FromResult(script[_next++](request));
        }
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
