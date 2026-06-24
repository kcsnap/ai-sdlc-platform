using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubAppTokenProviderTests
{
    private const string AppId = "123456";

    [Fact]
    public async Task Mints_token_via_JWT_and_resolves_the_org_installation()
    {
        var (provider, handler) = MakeProvider();

        var token = await provider.GetInstallationTokenAsync(CancellationToken.None);

        Assert.Equal("ghs_test_1", token);
        Assert.Equal(1, handler.InstallationsCalls);
        Assert.Equal(1, handler.TokenCalls);

        // The exchange call must authenticate with the App JWT (Bearer, three dot-separated segments).
        var auth = handler.LastAuth;
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal(3, auth.Parameter!.Split('.').Length);
    }

    [Fact]
    public async Task Caches_the_token_across_calls()
    {
        var (provider, handler) = MakeProvider();

        var first  = await provider.GetInstallationTokenAsync(CancellationToken.None);
        var second = await provider.GetInstallationTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, handler.TokenCalls);          // no second exchange
        Assert.Equal(1, handler.InstallationsCalls);  // installation resolved once
    }

    [Fact]
    public async Task Refreshes_near_expiry_without_re_resolving_the_installation()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-24T12:00:00Z"));
        var (provider, handler) = MakeProvider(time);

        var first = await provider.GetInstallationTokenAsync(CancellationToken.None);   // expires 13:00
        time.Advance(TimeSpan.FromMinutes(56));                                         // 12:56 → inside 5m skew
        var second = await provider.GetInstallationTokenAsync(CancellationToken.None);

        Assert.Equal("ghs_test_1", first);
        Assert.Equal("ghs_test_2", second);           // a fresh token was minted
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal(1, handler.InstallationsCalls);  // installation id cached across the refresh
    }

    private static (GitHubAppTokenProvider, RoutingHandler) MakeProvider(FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider(DateTimeOffset.Parse("2026-06-24T12:00:00Z"));
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();   // PKCS#1 "RSA PRIVATE KEY" — the GitHub App key shape

        var handler = new RoutingHandler(() => time.GetUtcNow().AddHours(1));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") };
        var provider = new GitHubAppTokenProvider(http, AppId, pem, "yorrixx-apps", time);
        return (provider, handler);
    }

    private sealed class RoutingHandler(Func<DateTimeOffset> expiresAt) : HttpMessageHandler
    {
        public int InstallationsCalls { get; private set; }
        public int TokenCalls { get; private set; }
        public AuthenticationHeaderValue? LastAuth { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuth = request.Headers.Authorization;
            var path = request.RequestUri!.AbsolutePath;
            string body;

            if (path == "/app/installations")
            {
                InstallationsCalls++;
                body = """[{"id":42,"account":{"login":"yorrixx-apps"}}]""";
            }
            else if (path == "/app/installations/42/access_tokens")
            {
                TokenCalls++;
                body = $$"""{"token":"ghs_test_{{TokenCalls}}","expires_at":"{{expiresAt():O}}"}""";
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
