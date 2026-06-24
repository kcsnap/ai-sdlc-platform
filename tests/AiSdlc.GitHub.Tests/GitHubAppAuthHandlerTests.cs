using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubAppAuthHandlerTests
{
    [Fact]
    public async Task Sets_the_installation_token_as_a_bearer_header_on_each_request()
    {
        using var rsa = RSA.Create(2048);
        var provider = new GitHubAppTokenProvider(
            new HttpClient(new TokenStubHandler()) { BaseAddress = new Uri("https://api.github.test") },
            "123456", rsa.ExportRSAPrivateKeyPem(), "yorrixx-apps", TimeProvider.System);

        var capture = new CaptureHandler();
        var client  = new HttpClient(new GitHubAppAuthHandler(provider) { InnerHandler = capture })
        {
            BaseAddress = new Uri("https://api.github.test")
        };

        await client.GetAsync("/repos/yorrixx-apps/user-app-x");

        Assert.Equal("Bearer", capture.LastAuth!.Scheme);
        Assert.Equal("ghs_installation_token", capture.LastAuth.Parameter);
    }

    // Answers the provider's own /app/installations + /access_tokens calls.
    private sealed class TokenStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path == "/app/installations"
                ? """[{"id":42,"account":{"login":"yorrixx-apps"}}]"""
                : """{"token":"ghs_installation_token","expires_at":"2099-01-01T00:00:00Z"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? LastAuth { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuth = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
