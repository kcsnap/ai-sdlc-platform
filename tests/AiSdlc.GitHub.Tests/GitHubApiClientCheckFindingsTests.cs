using System.Net;
using AiSdlc.GitHub;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubApiClientCheckFindingsTests
{
    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.github.com") };

    private const string CheckRunsBody = """
        { "check_runs": [
            { "id": 41, "name": "build-frontend", "status": "completed", "conclusion": "success",
              "details_url": "https://github.com/org/repo/actions/runs/9/job/76" },
            { "id": 42, "name": "build-api", "status": "completed", "conclusion": "failure",
              "details_url": "https://github.com/org/repo/actions/runs/9/job/77" }
        ] }
        """;

    [Fact]
    public async Task Fetches_annotations_for_failed_checks_only()
    {
        var handler = new SequentialHandler(
            new FakeResponse(HttpStatusCode.OK, CheckRunsBody),
            new FakeResponse(HttpStatusCode.OK, """
                [ { "path": "src/api/Program.cs", "start_line": 12, "annotation_level": "failure",
                    "message": "CS0103: The name 'Foo' does not exist in the current context" } ]
                """));

        var client   = new GitHubApiClient(MakeClient(handler));
        var findings = await client.GetFailedCheckFindingsAsync("org/repo", "abc123", CancellationToken.None);

        var annotationRequest = handler.Requests[1];
        Assert.Contains("/repos/org/repo/check-runs/42/annotations", annotationRequest.RequestUri!.ToString());
        Assert.Equal(2, handler.Requests.Count); // no annotation fetch for the successful check

        var finding = Assert.Single(findings);
        Assert.Equal("build-api", finding.CheckName);
        var annotation = Assert.Single(finding.Annotations);
        Assert.Equal("src/api/Program.cs", annotation.Path);
        Assert.Equal(12, annotation.StartLine);
        Assert.Contains("CS0103", annotation.Message);
    }

    [Fact]
    public async Task Empty_annotations_fall_back_to_the_job_log_tail()
    {
        var handler = new SequentialHandler(
            new FakeResponse(HttpStatusCode.OK, CheckRunsBody),
            new FakeResponse(HttpStatusCode.OK, "[]"),
            new FakeResponse(HttpStatusCode.OK, "line1\nerror TS2304: Cannot find name 'Foo'.\nline3"));

        var client   = new GitHubApiClient(MakeClient(handler));
        var findings = await client.GetFailedCheckFindingsAsync("org/repo", "abc123", CancellationToken.None);

        Assert.Contains("/repos/org/repo/actions/jobs/77/logs", handler.Requests[2].RequestUri!.ToString());
        Assert.Contains("TS2304", Assert.Single(findings).LogTail);
    }

    [Fact]
    public async Task Content_free_annotations_also_fall_back_to_the_job_log_tail()
    {
        // Observed live (b2fb2ed7#17): the runner emits "Process completed with exit code 1"
        // and deprecation warnings as annotations — non-empty but useless. The real error
        // (NU1605) only existed in the job log.
        var handler = new SequentialHandler(
            new FakeResponse(HttpStatusCode.OK, CheckRunsBody),
            new FakeResponse(HttpStatusCode.OK, """
                [ { "path": ".github", "start_line": 2, "annotation_level": "warning",
                    "message": "Node.js 20 actions are deprecated." },
                  { "path": ".github", "start_line": 28, "annotation_level": "failure",
                    "message": "Process completed with exit code 1." } ]
                """),
            new FakeResponse(HttpStatusCode.OK, "restore...\nApi.csproj : error NU1605: Detected package downgrade: Azure.Identity\n1 Error(s)"));

        var client   = new GitHubApiClient(MakeClient(handler));
        var findings = await client.GetFailedCheckFindingsAsync("org/repo", "abc123", CancellationToken.None);

        Assert.Contains("/repos/org/repo/actions/jobs/77/logs", handler.Requests[2].RequestUri!.ToString());
        Assert.Contains("NU1605", Assert.Single(findings).LogTail);
    }

    [Theory]
    [InlineData("failure", "CS0103: The name 'Foo' does not exist", true)]
    [InlineData("failure", "Process completed with exit code 1.", false)]
    [InlineData("warning", "Node.js 20 actions are deprecated.", false)]
    public void Substantive_annotation_classification(string level, string message, bool expect)
    {
        Assert.Equal(expect, GitHubApiClient.IsSubstantiveAnnotation(level, message));
    }

    [Fact]
    public async Task Annotation_fetch_error_degrades_to_an_empty_finding_not_an_exception()
    {
        var handler = new SequentialHandler(
            new FakeResponse(HttpStatusCode.OK, CheckRunsBody),
            new FakeResponse(HttpStatusCode.InternalServerError, "{}"));

        var client   = new GitHubApiClient(MakeClient(handler));
        var findings = await client.GetFailedCheckFindingsAsync("org/repo", "abc123", CancellationToken.None);

        var finding = Assert.Single(findings);
        Assert.Equal("build-api", finding.CheckName);
        Assert.Empty(finding.Annotations);
        Assert.Null(finding.LogTail);
    }

    [Theory]
    [InlineData("https://github.com/org/repo/actions/runs/9/job/77", true, 77)]
    [InlineData("https://github.com/org/repo/actions/runs/9", false, 0)]
    [InlineData("https://example.com/something-else", false, 0)]
    [InlineData(null, false, 0)]
    public void Job_id_parses_from_details_url(string? url, bool expectOk, long expectedId)
    {
        var ok = GitHubApiClient.TryParseJobIdFromDetailsUrl(url, out var jobId);
        Assert.Equal(expectOk, ok);
        if (expectOk) Assert.Equal(expectedId, jobId);
    }

    [Fact]
    public void Log_tail_takes_the_last_lines_and_caps_chars_from_the_front()
    {
        var log  = string.Join('\n', Enumerable.Range(1, 300).Select(i => $"line-{i}"));
        var tail = GitHubApiClient.TakeLogTail(log, 150, 6000);

        Assert.DoesNotContain("line-150\n", tail + "\n");
        Assert.Contains("line-300", tail);

        var capped = GitHubApiClient.TakeLogTail(new string('x', 10_000), 150, 100);
        Assert.Equal(100, capped.Length);
    }

    [Fact]
    public async Task Newest_open_ai_pr_is_found_by_branch_prefix()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            [ { "number": 16, "title": "x", "state": "open",
                "head": { "ref": "dependabot/npm", "sha": "aaa" }, "base": { "ref": "main" },
                "user": { "login": "bot" }, "draft": false, "labels": [],
                "html_url": "https://x", "created_at": "2026-06-12T14:00:00Z" },
              { "number": 14, "title": "y", "state": "open",
                "head": { "ref": "ai/13-build-app", "sha": "bbb" }, "base": { "ref": "main" },
                "user": { "login": "kcsnap" }, "draft": false, "labels": [],
                "html_url": "https://y", "created_at": "2026-06-12T13:00:00Z" } ]
            """);

        var client = new GitHubApiClient(MakeClient(handler));
        var pr = await client.GetNewestOpenPullRequestByBranchPrefixAsync("org/repo", "ai/", CancellationToken.None);

        Assert.NotNull(pr);
        Assert.Equal(14, pr.Number);
        Assert.Equal("ai/13-build-app", pr.HeadBranch);
        Assert.Equal("bbb", pr.HeadSha);
    }

    [Fact]
    public async Task No_open_ai_pr_returns_null()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "[]");
        var client  = new GitHubApiClient(MakeClient(handler));
        Assert.Null(await client.GetNewestOpenPullRequestByBranchPrefixAsync("org/repo", "ai/", CancellationToken.None));
    }

    [Fact]
    public async Task Check_run_id_is_mapped()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, CheckRunsBody);
        var client  = new GitHubApiClient(MakeClient(handler));

        var checks = await client.GetCheckRunResultsAsync("org/repo", "abc123", CancellationToken.None);

        Assert.Equal(42, checks.Single(c => c.Name == "build-api").Id);
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
