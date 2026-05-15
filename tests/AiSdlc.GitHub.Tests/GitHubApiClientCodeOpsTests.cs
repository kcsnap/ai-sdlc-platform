using System.Net;
using AiSdlc.GitHub;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubApiClientCodeOpsTests
{
    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.github.com") };

    [Fact]
    public async Task GetDefaultBranchShaAsync_ReturnsSha()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            {
                "ref": "refs/heads/main",
                "object": {
                    "sha": "abc123def456",
                    "type": "commit"
                }
            }
            """);

        var client = new GitHubApiClient(MakeClient(handler));
        var sha    = await client.GetDefaultBranchShaAsync("org/repo", "main", CancellationToken.None);

        Assert.Equal("abc123def456", sha);
    }

    [Fact]
    public async Task CreateBranchAsync_SucceedsOnCreated()
    {
        var handler = new FakeHandler(HttpStatusCode.Created, """
            { "ref": "refs/heads/ai/1-feature", "object": { "sha": "abc123" } }
            """);

        var client = new GitHubApiClient(MakeClient(handler));
        // Should not throw
        await client.CreateBranchAsync("org/repo", "ai/1-feature", "abc123", CancellationToken.None);
    }

    [Fact]
    public async Task CreateBranchAsync_DoesNotThrowOn422()
    {
        // Branch already exists — should be silently ignored
        var handler = new FakeHandler(HttpStatusCode.UnprocessableEntity, """
            { "message": "Reference already exists" }
            """);

        var client = new GitHubApiClient(MakeClient(handler));
        // Should not throw
        await client.CreateBranchAsync("org/repo", "ai/1-feature", "abc123", CancellationToken.None);
    }

    [Fact]
    public async Task CreateOrUpdateFileAsync_NewFile_SendsBase64ContentWithoutSha()
    {
        // First call (GET) returns 404 → new file
        // Second call (PUT) returns 201 → created
        var handler = new SequentialHandler(
            new FakeResponse(HttpStatusCode.NotFound, "{}"),
            new FakeResponse(HttpStatusCode.Created, """
                { "content": { "name": "README.md" } }
                """));

        var client = new GitHubApiClient(MakeClient(handler));

        await client.CreateOrUpdateFileAsync(
            "org/repo", "README.md", "# Hello", "feat: add readme", "ai/1-feature",
            CancellationToken.None);

        var putRequest = handler.Requests.FirstOrDefault(r => r.Method == HttpMethod.Put);
        Assert.NotNull(putRequest);

        var body = await putRequest.Content!.ReadAsStringAsync();
        Assert.Contains("content", body);
        // "# Hello" in base64 is "IyBIZWxsbw=="
        Assert.Contains("IyBIZWxsbw==", body);
        // No sha when creating a new file
        Assert.DoesNotContain("\"sha\"", body);
    }

    [Fact]
    public async Task CreateOrUpdateFileAsync_ExistingFile_IncludesBlobSha()
    {
        // First call (GET) returns 200 with existing sha
        var handler = new SequentialHandler(
            new FakeResponse(HttpStatusCode.OK, """
                { "encoding": "base64", "content": "SGVsbG8=", "sha": "blob-sha-123" }
                """),
            new FakeResponse(HttpStatusCode.OK, """
                { "content": { "name": "README.md" } }
                """));

        var client = new GitHubApiClient(MakeClient(handler));

        await client.CreateOrUpdateFileAsync(
            "org/repo", "README.md", "# Updated", "feat: update readme", "main",
            CancellationToken.None);

        var putRequest = handler.Requests.FirstOrDefault(r => r.Method == HttpMethod.Put);
        Assert.NotNull(putRequest);

        var body = await putRequest.Content!.ReadAsStringAsync();
        Assert.Contains("blob-sha-123", body);
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed record FakeResponse(HttpStatusCode Status, string Body);

    private sealed class SequentialHandler(params FakeResponse[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var r = responses[Math.Min(_index++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(r.Status)
            {
                Content = new StringContent(r.Body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
