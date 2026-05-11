using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiSdlc.GitHub;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubApiClientTests
{
    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.github.com") };

    [Fact]
    public async Task GetIssueAsync_MapsFieldsCorrectly()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            {
                "number": 42,
                "title": "Add feature X",
                "body": "Feature description",
                "state": "open",
                "user": { "login": "alice" },
                "labels": [{ "name": "bug" }, { "name": "enhancement" }],
                "html_url": "https://github.com/org/repo/issues/42",
                "created_at": "2024-06-01T00:00:00Z",
                "updated_at": null
            }
            """);

        var client = new GitHubApiClient(MakeClient(handler));
        var result = await client.GetIssueAsync("org/repo", 42, CancellationToken.None);

        Assert.Equal(42,         result.Issue.IssueNumber);
        Assert.Equal("org/repo", result.Issue.Repository);
        Assert.Equal("Add feature X", result.Title);
        Assert.Equal("Feature description", result.BodyMarkdown);
        Assert.Equal("open",   result.State);
        Assert.Equal("alice",  result.AuthorLogin);
        Assert.Equal(2,        result.Labels.Count);
        Assert.Contains("bug", result.Labels);
    }

    [Fact]
    public async Task GetIssueCommentsAsync_ReturnsMappedComments()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            [
                {
                    "id": 1001,
                    "body": "/approve-brief",
                    "user": { "login": "bob" },
                    "html_url": "https://github.com/org/repo/issues/1#issuecomment-1001",
                    "created_at": "2024-06-01T12:00:00Z",
                    "updated_at": null
                }
            ]
            """);

        var client   = new GitHubApiClient(MakeClient(handler));
        var comments = await client.GetIssueCommentsAsync("org/repo", 1, CancellationToken.None);

        Assert.Single(comments);
        Assert.Equal(1001L, comments[0].CommentId);
        Assert.Equal("/approve-brief", comments[0].BodyMarkdown);
        Assert.Equal("bob", comments[0].AuthorLogin);
    }

    [Fact]
    public async Task AddIssueCommentAsync_PostsBodyAndReturnsComment()
    {
        var handler = new FakeHandler(HttpStatusCode.Created, """
            {
                "id": 2001,
                "body": "Hello from AI SDLC",
                "user": { "login": "ai-sdlc[bot]" },
                "html_url": "https://github.com/org/repo/issues/5#issuecomment-2001",
                "created_at": "2024-06-01T12:00:00Z",
                "updated_at": null
            }
            """);

        var client  = new GitHubApiClient(MakeClient(handler));
        var comment = await client.AddIssueCommentAsync("org/repo", 5, "Hello from AI SDLC", CancellationToken.None);

        Assert.Equal(2001L, comment.CommentId);
        Assert.Equal("Hello from AI SDLC", comment.BodyMarkdown);
        Assert.Equal("org/repo", comment.Repository);
        Assert.Equal(5, comment.IssueOrPullRequestNumber);
    }

    [Fact]
    public async Task GetChangedFilesAsync_MapsFilesCorrectly()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            [
                {
                    "filename": "src/Foo.cs",
                    "status": "modified",
                    "additions": 10,
                    "deletions": 2,
                    "changes": 12
                }
            ]
            """);

        var client = new GitHubApiClient(MakeClient(handler));
        var files  = await client.GetChangedFilesAsync("org/repo", 7, CancellationToken.None);

        Assert.Single(files);
        Assert.Equal("src/Foo.cs", files[0].Path);
        Assert.Equal("modified",   files[0].Status);
        Assert.Equal(10,           files[0].Additions);
    }

    [Fact]
    public async Task GetCheckRunResultsAsync_MapsRunsCorrectly()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            {
                "check_runs": [
                    {
                        "name": "dotnet-test",
                        "status": "completed",
                        "conclusion": "success",
                        "details_url": "https://github.com/actions/runs/1"
                    }
                ]
            }
            """);

        var client = new GitHubApiClient(MakeClient(handler));
        var runs   = await client.GetCheckRunResultsAsync("org/repo", "main", CancellationToken.None);

        Assert.Single(runs);
        Assert.Equal("dotnet-test", runs[0].Name);
        Assert.Equal("success",     runs[0].Conclusion);
    }

    // Minimal fake HttpMessageHandler — returns a fixed status code and body.
    private sealed class FakeHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
