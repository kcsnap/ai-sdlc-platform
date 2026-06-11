using System.Net;
using System.Text;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubTransientRetryHandlerTests
{
    private static readonly TimeSpan[] NoDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    [Fact]
    public async Task TransientUnauthorized_IsRetried_UntilSuccess()
    {
        // The 2026-06-10 GitHub incident shape: valid token, intermittent 401s.
        var inner = new SequentialHandler(
        [
            (HttpStatusCode.Unauthorized, """{"message":"Requires authentication"}"""),
            (HttpStatusCode.Unauthorized, """{"message":"Requires authentication"}"""),
            (HttpStatusCode.OK, "{}")
        ]);
        using var client = MakeClient(inner);

        var response = await client.PostAsync("https://api.github.test/repos/o/r/issues/1/labels",
            new StringContent("""{"labels":["x"]}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task PostBody_SurvivesRetries()
    {
        var inner = new SequentialHandler(
        [
            (HttpStatusCode.ServiceUnavailable, ""),
            (HttpStatusCode.OK, "{}")
        ]);
        using var client = MakeClient(inner);

        await client.PostAsync("https://api.github.test/x",
            new StringContent("payload-123", Encoding.UTF8, "application/json"));

        Assert.Equal(2, inner.CallCount);
        Assert.All(inner.SeenBodies, b => Assert.Equal("payload-123", b));
    }

    [Fact]
    public async Task NonTransientStatus_IsNotRetried()
    {
        var inner = new SequentialHandler([(HttpStatusCode.NotFound, "{}"), (HttpStatusCode.OK, "{}")]);
        using var client = MakeClient(inner);

        var response = await client.GetAsync("https://api.github.test/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task PersistentFailure_ReturnsFinalResponse_AfterAllRetries()
    {
        var responses = Enumerable.Repeat((HttpStatusCode.Unauthorized, "{}"), 10).ToList();
        var inner = new SequentialHandler(responses);
        using var client = MakeClient(inner);

        var response = await client.GetAsync("https://api.github.test/x");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(4, inner.CallCount); // initial attempt + 3 retries
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void IsTransient_ClassifiesCorrectly(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, GitHubTransientRetryHandler.IsTransient(status));
    }

    private static HttpClient MakeClient(HttpMessageHandler inner) =>
        new(new GitHubTransientRetryHandler(NoDelays) { InnerHandler = inner });

    private sealed class SequentialHandler(List<(HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
    {
        private int _index;
        public int CallCount => _index;
        public List<string> SeenBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                SeenBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            var (status, body) = responses[Math.Min(_index++, responses.Count - 1)];
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
