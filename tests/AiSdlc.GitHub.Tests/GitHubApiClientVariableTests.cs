using System.Net;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubApiClientVariableTests
{
    [Fact]
    public async Task SetRepoVariableAsync_creates_via_POST()
    {
        var handler = new ScriptHandler(req => req.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
        var client  = new GitHubApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") });

        await client.SetRepoVariableAsync("o/r", "AZURE_CLIENT_ID", "abc", CancellationToken.None);

        Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Calls[0].Method);
        Assert.Equal("/repos/o/r/actions/variables", handler.Calls[0].Path);
        Assert.Contains("AZURE_CLIENT_ID", handler.Calls[0].Body);
        Assert.Contains("abc", handler.Calls[0].Body);
    }

    [Fact]
    public async Task SetRepoVariableAsync_updates_via_PATCH_on_conflict()
    {
        var handler = new ScriptHandler(req =>
            req.Method == HttpMethod.Post  ? HttpStatusCode.Conflict :
            req.Method == HttpMethod.Patch ? HttpStatusCode.NoContent : HttpStatusCode.InternalServerError);
        var client = new GitHubApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") });

        await client.SetRepoVariableAsync("o/r", "AZURE_CLIENT_ID", "abc", CancellationToken.None);

        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal(HttpMethod.Patch, handler.Calls[1].Method);
        Assert.Equal("/repos/o/r/actions/variables/AZURE_CLIENT_ID", handler.Calls[1].Path);
    }

    private sealed class ScriptHandler(Func<HttpRequestMessage, HttpStatusCode> status) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return new HttpResponseMessage(status(request));
        }
    }
}
