using System.Net;
using AiSdlc.GitHub;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubApiClientRepoCreateTests
{
    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.github.com") };

    [Fact]
    public async Task CreateRepositoryFromTemplateAsync_ReturnsCreatedRepository()
    {
        var handler = new SequentialHandler(new FakeResponse(HttpStatusCode.Created, """
            {
                "full_name": "yorrixx-apps/user-app-dd0e9574",
                "html_url": "https://github.com/yorrixx-apps/user-app-dd0e9574",
                "default_branch": "main",
                "id": 1303218647,
                "owner": { "id": 289196324, "login": "yorrixx-apps" }
            }
            """));

        var client = new GitHubApiClient(MakeClient(handler));
        var repo = await client.CreateRepositoryFromTemplateAsync(
            "yorrixx-apps/template", "yorrixx-apps", "user-app-dd0e9574", isPrivate: true, "desc", CancellationToken.None);

        Assert.Equal("yorrixx-apps/user-app-dd0e9574", repo.FullName);
        Assert.Equal("main", repo.DefaultBranch);
        // F5: immutable ids feed the second (immutable-subject) federated credential.
        Assert.Equal(1303218647, repo.RepoId);
        Assert.Equal(289196324, repo.OwnerId);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreateRepositoryFromTemplateAsync_NameAlreadyExists_ReturnsExistingRepo()
    {
        // Ensure semantics (G6 P3): a re-kicked create-build hits GitHub 422 "Name already exists" — the
        // client must fetch the existing repo and return it as success, not throw.
        var handler = new SequentialHandler(
            new FakeResponse(HttpStatusCode.UnprocessableEntity, """
                { "message": "Repository creation failed.", "errors": [ { "message": "Name already exists on this account" } ] }
                """),
            new FakeResponse(HttpStatusCode.OK, """
                {
                    "full_name": "yorrixx-apps/user-app-dd0e9574",
                    "html_url": "https://github.com/yorrixx-apps/user-app-dd0e9574",
                    "default_branch": "main",
                    "id": 1303218647,
                    "owner": { "id": 289196324, "login": "yorrixx-apps" }
                }
                """));

        var client = new GitHubApiClient(MakeClient(handler));
        var repo = await client.CreateRepositoryFromTemplateAsync(
            "yorrixx-apps/template", "yorrixx-apps", "user-app-dd0e9574", isPrivate: true, "desc", CancellationToken.None);

        Assert.Equal("yorrixx-apps/user-app-dd0e9574", repo.FullName);
        Assert.Equal(1303218647, repo.RepoId);
        Assert.Equal(289196324, repo.OwnerId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/repos/yorrixx-apps/user-app-dd0e9574", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CreateRepositoryFromTemplateAsync_OtherFailure_Throws()
    {
        var handler = new SequentialHandler(new FakeResponse(HttpStatusCode.Forbidden, """{ "message": "Resource not accessible" }"""));

        var client = new GitHubApiClient(MakeClient(handler));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateRepositoryFromTemplateAsync(
            "yorrixx-apps/template", "yorrixx-apps", "user-app-dd0e9574", isPrivate: true, "desc", CancellationToken.None));
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
