using System.Net;
using AiSdlc.GitHub;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubApiClientMergeTests
{
    private static HttpClient MakeClient(CapturingHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.github.com") };

    [Fact]
    public async Task MergePullRequestAsync_SendsPutToCorrectUrl()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var client  = new GitHubApiClient(MakeClient(handler));

        await client.MergePullRequestAsync("org/repo", 42, "feat: my change (closes #7)", CancellationToken.None);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.EndsWith("/repos/org/repo/pulls/42/merge", handler.LastUri?.ToString());
    }

    [Fact]
    public async Task MergePullRequestAsync_ThrowsOnNonSuccess()
    {
        var handler = new CapturingHandler(HttpStatusCode.MethodNotAllowed, "{\"message\":\"Pull Request is not mergeable\"}");
        var client  = new GitHubApiClient(MakeClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.MergePullRequestAsync("org/repo", 42, "msg", CancellationToken.None));
    }

    private sealed class CapturingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public HttpMethod?  LastMethod { get; private set; }
        public Uri?         LastUri    { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri    = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
