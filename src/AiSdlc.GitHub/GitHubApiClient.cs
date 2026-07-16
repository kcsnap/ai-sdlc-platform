using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiSdlc.Shared;

namespace AiSdlc.GitHub;

public sealed class GitHubApiClient : IGitHubService
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GitHubApiClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public async Task<IssueDetails> GetIssueAsync(string repository, int issueNumber, CancellationToken cancellationToken)
    {
        var json = await GetAsync<IssueJson>($"/repos/{repository}/issues/{issueNumber}", cancellationToken);
        return MapIssue(repository, json);
    }

    public async Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string repository, int issueNumber, CancellationToken cancellationToken)
    {
        var json = await GetAsync<CommentJson[]>($"/repos/{repository}/issues/{issueNumber}/comments?per_page=100", cancellationToken);
        return json.Select(c => MapComment(repository, issueNumber, c)).ToArray();
    }

    public async Task<IssueComment> AddIssueCommentAsync(string repository, int issueNumber, string markdown, CancellationToken cancellationToken)
    {
        var json = await PostAsync<CommentJson>(
            $"/repos/{repository}/issues/{issueNumber}/comments",
            new { body = markdown },
            cancellationToken);
        return MapComment(repository, issueNumber, json);
    }

    public async Task<IssueComment> AddPullRequestCommentAsync(string repository, int pullRequestNumber, string markdown, CancellationToken cancellationToken)
    {
        // GitHub's issue comment endpoint works for both issues and PRs
        var json = await PostAsync<CommentJson>(
            $"/repos/{repository}/issues/{pullRequestNumber}/comments",
            new { body = markdown },
            cancellationToken);
        return MapComment(repository, pullRequestNumber, json);
    }

    public async Task<IReadOnlyList<string>> AddLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var json = await PostAsync<LabelJson[]>(
            $"/repos/{repository}/issues/{issueOrPrNumber}/labels",
            new { labels },
            cancellationToken);
        return json.Select(l => l.Name).ToArray();
    }

    public async Task<IReadOnlyList<string>> RemoveLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var remaining = new List<string>();
        foreach (var label in labels)
        {
            using var response = await _http.DeleteAsync(
                $"/repos/{repository}/issues/{issueOrPrNumber}/labels/{Uri.EscapeDataString(label)}",
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<LabelJson[]>(JsonOptions, cancellationToken) ?? [];
                remaining.AddRange(json.Select(l => l.Name));
            }
        }
        return remaining.Distinct().ToArray();
    }

    public async Task<GitHubPullRequestReference> CreatePullRequestAsync(CreatePullRequestRequest request, CancellationToken cancellationToken)
    {
        PullRequestJson json;
        try
        {
            json = await PostAsync<PullRequestJson>(
                $"/repos/{request.Repository}/pulls",
                new
                {
                    title  = request.Title,
                    body   = request.BodyMarkdown,
                    head   = request.HeadBranch,
                    @base  = request.BaseBranch,
                    draft  = request.Draft
                },
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("A pull request already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent create: a reopened-issue re-run reuses the same work branch, and the
            // previous run's PR may still be open. Adopt it instead of failing the orchestration
            // (observed on user-app-7301476a, 2026-06-11 — the same lesson as Graph FICs: lookups
            // are cheap, duplicate-create failures kill whole runs).
            var owner = request.Repository.Split('/')[0];
            var open  = await GetAsync<PullRequestJson[]>(
                $"/repos/{request.Repository}/pulls?head={Uri.EscapeDataString($"{owner}:{request.HeadBranch}")}&state=open",
                cancellationToken);
            json = open.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"GitHub reported an existing PR for {request.HeadBranch} but none is open.");
        }

        if (request.Labels.Count > 0)
        {
            await AddLabelsAsync(request.Repository, json.Number, request.Labels, cancellationToken);
        }

        return new GitHubPullRequestReference(request.Repository, json.Number, request.HeadBranch, json.HtmlUrl);
    }

    public async Task<PullRequestDetails> GetPullRequestAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken)
    {
        var json = await GetAsync<PullRequestJson>($"/repos/{repository}/pulls/{pullRequestNumber}", cancellationToken);
        return MapPullRequest(repository, json);
    }

    public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int pullRequestNumber, CancellationToken cancellationToken)
    {
        var json = await GetAsync<ChangedFileJson[]>($"/repos/{repository}/pulls/{pullRequestNumber}/files?per_page=100", cancellationToken);
        return json.Select(f => new ChangedFile
        {
            Path      = f.Filename,
            Status    = f.Status,
            Additions = f.Additions,
            Deletions = f.Deletions,
            Changes   = f.Changes
        }).ToArray();
    }

    public async Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string repository, string reference, CancellationToken cancellationToken)
    {
        var json = await GetAsync<CheckRunsJson>($"/repos/{repository}/commits/{Uri.EscapeDataString(reference)}/check-runs?per_page=100", cancellationToken);
        return json.CheckRuns.Select(cr => new CheckRunResult
        {
            Id         = cr.Id,
            Name       = cr.Name,
            Status     = cr.Status,
            Conclusion = cr.Conclusion ?? "pending",
            DetailsUrl = cr.DetailsUrl
        }).ToArray();
    }

    public async Task<OpenPullRequestInfo?> GetNewestOpenPullRequestByBranchPrefixAsync(
        string repository, string branchPrefix, CancellationToken cancellationToken)
    {
        var prs = await GetAsync<PullRequestJson[]>(
            $"/repos/{repository}/pulls?state=open&sort=created&direction=desc&per_page=20", cancellationToken);
        var match = prs.FirstOrDefault(p =>
            p.Head.Ref.StartsWith(branchPrefix, StringComparison.Ordinal) && p.Head.Sha is not null);
        return match is null
            ? null
            : new OpenPullRequestInfo(match.Number, match.Head.Ref, match.Head.Sha!);
    }

    internal const int LogTailLines    = 150;
    internal const int LogTailMaxChars = 6_000;
    private  const long LogFetchMaxBytes = 5_000_000; // job logs can be multi-MB; skip rather than buffer

    public async Task<IReadOnlyList<FailedCheckFinding>> GetFailedCheckFindingsAsync(
        string repository, string reference, CancellationToken cancellationToken)
    {
        var checks   = await GetCheckRunResultsAsync(repository, reference, cancellationToken);
        var findings = new List<FailedCheckFinding>();

        foreach (var check in checks.Where(c => IsFailedCheck(c.Status, c.Conclusion)))
        {
            IReadOnlyList<CheckAnnotation> annotations = [];
            string? logTail = null;
            try
            {
                var json = await GetAsync<AnnotationJson[]>(
                    $"/repos/{repository}/check-runs/{check.Id}/annotations?per_page=100", cancellationToken);
                annotations = json
                    .Select(a => new CheckAnnotation(a.Path, a.StartLine, a.AnnotationLevel, a.Message))
                    .ToArray();

                // Annotations only carry compiler errors when the workflow has a problem
                // matcher that engaged. Runner-generated annotations ("Process completed
                // with exit code 1", deprecation warnings) are content-free — observed live
                // on b2fb2ed7#17, where the repair agent got nothing actionable while the
                // job log held the real NU1605 error. Fall back to the log tail whenever no
                // SUBSTANTIVE annotation exists.
                if (!annotations.Any(a => IsSubstantiveAnnotation(a.Level, a.Message)) &&
                    TryParseJobIdFromDetailsUrl(check.DetailsUrl, out var jobId))
                {
                    var log = await GetTextAsync($"/repos/{repository}/actions/jobs/{jobId}/logs", cancellationToken);
                    if (log is not null)
                        logTail = TakeLogTail(log, LogTailLines, LogTailMaxChars);
                }
            }
            catch (HttpRequestException)
            {
                // Degrade: the finding carries only the check name; the renderer skips it.
            }
            findings.Add(new FailedCheckFinding(check.Name, annotations, logTail));
        }

        return findings;
    }

    // Single source of truth for "this check failed" — the merge gate and the findings
    // extractor must never disagree about what counts as a failure.
    public static bool IsFailedCheck(string status, string conclusion) =>
        status == "completed" && conclusion is not ("success" or "neutral" or "skipped");

    // An annotation is substantive when it can actually guide a repair: failure-level and
    // not the runner's generic exit message. Shared by the client (log-tail fallback
    // decision) and the findings renderer (what to show the repair agent).
    public static bool IsSubstantiveAnnotation(string level, string message) =>
        string.Equals(level, "failure", StringComparison.OrdinalIgnoreCase)
        && !message.StartsWith("Process completed with exit code", StringComparison.OrdinalIgnoreCase);

    // details_url: https://github.com/{org}/{repo}/actions/runs/{runId}/job/{jobId}
    internal static bool TryParseJobIdFromDetailsUrl(string? detailsUrl, out long jobId)
    {
        jobId = 0;
        if (string.IsNullOrEmpty(detailsUrl)) return false;
        var match = Regex.Match(detailsUrl, @"/actions/runs/\d+/job/(\d+)");
        return match.Success && long.TryParse(match.Groups[1].Value, out jobId);
    }

    internal static string TakeLogTail(string log, int maxLines, int maxChars)
    {
        var lines = log.Split('\n');
        var tail  = string.Join('\n', lines.Skip(Math.Max(0, lines.Length - maxLines))).TrimEnd();
        return tail.Length <= maxChars ? tail : tail[^maxChars..];
    }

    private async Task<string?> GetTextAsync(string path, CancellationToken cancellationToken)
    {
        // The Actions logs endpoint 302-redirects to a signed blob URL; the default handler
        // follows it (and correctly drops the Authorization header cross-host).
        using var response = await _http.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (response.Content.Headers.ContentLength > LogFetchMaxBytes)
            return null;
        await EnsureSuccessAsync(response, $"GET {path}", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RepoTreeEntry>> GetBranchFileTreeAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var json = await GetAsync<TreeJson>(
            $"/repos/{repository}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1", cancellationToken);
        return json.Tree
            .Where(t => t.Type == "blob")
            .Select(t => new RepoTreeEntry(t.Path, t.Size ?? 0))
            .ToArray();
    }

    public async Task MergePullRequestAsync(string repository, int pullRequestNumber, string commitMessage, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(
            $"/repos/{repository}/pulls/{pullRequestNumber}/merge",
            new { merge_method = "squash", commit_message = commitMessage },
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, $"PUT /repos/{repository}/pulls/{pullRequestNumber}/merge", cancellationToken);
    }

    public async Task<string> GetDefaultBranchAsync(string repository, CancellationToken cancellationToken)
    {
        var json = await GetAsync<RepositoryJson>($"/repos/{repository}", cancellationToken);
        return json.DefaultBranch;
    }

    public async Task<string> GetDefaultBranchShaAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var json = await GetAsync<GitRefJson>($"/repos/{repository}/git/refs/heads/{Uri.EscapeDataString(branch)}", cancellationToken);
        return json.Object.Sha;
    }

    public async Task CreateBranchAsync(string repository, string branchName, string sha, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"/repos/{repository}/git/refs",
                new { @ref = $"refs/heads/{branchName}", sha },
                JsonOptions,
                cancellationToken);
            await EnsureSuccessAsync(response, $"POST /repos/{repository}/git/refs", cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Branch already exists — force-reset it to the requested base instead of reusing
            // it. A leftover branch from an earlier run diverges permanently once that run's
            // PR is squash-merged, and every later run that commits onto it produces a PR with
            // merge conflicts (user-app-7301476a, 2026-06-11: auto-merge died on 405
            // "Pull Request has merge conflicts"). Each run must start from the current base.
            using var reset = await _http.PatchAsJsonAsync(
                $"/repos/{repository}/git/refs/heads/{Uri.EscapeDataString(branchName)}",
                new { sha, force = true },
                JsonOptions,
                cancellationToken);
            await EnsureSuccessAsync(reset, $"PATCH /repos/{repository}/git/refs/heads/{branchName}", cancellationToken);
        }
    }

    public async Task CreateOrUpdateFileAsync(string repository, string path, string content, string commitMessage, string branch, CancellationToken cancellationToken)
    {
        string? existingBlobSha = null;
        using var getResponse = await _http.GetAsync($"/repos/{repository}/contents/{path}?ref={Uri.EscapeDataString(branch)}", cancellationToken);
        if (getResponse.IsSuccessStatusCode)
        {
            var existing = await getResponse.Content.ReadFromJsonAsync<FileContentJson>(JsonOptions, cancellationToken);
            existingBlobSha = existing?.Sha;
        }

        var body = new
        {
            message = commitMessage,
            content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)),
            branch,
            sha     = existingBlobSha
        };

        using var putResponse = await _http.PutAsJsonAsync($"/repos/{repository}/contents/{path}", body, JsonOptions, cancellationToken);
        if (!putResponse.IsSuccessStatusCode)
        {
            var detail = await putResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"GitHub returned {(int)putResponse.StatusCode} ({putResponse.ReasonPhrase}) committing '{path}'. Detail: {detail}",
                inner: null, putResponse.StatusCode);
        }
    }

    public Task<string?> GetFileContentAsync(string repository, string path, CancellationToken cancellationToken) =>
        GetFileContentAtRefAsync(repository, path, null, cancellationToken);

    public Task<string?> GetBranchFileContentAsync(string repository, string path, string branch, CancellationToken cancellationToken) =>
        GetFileContentAtRefAsync(repository, path, branch, cancellationToken);

    private async Task<string?> GetFileContentAtRefAsync(string repository, string path, string? gitRef, CancellationToken cancellationToken)
    {
        var url = gitRef is null
            ? $"/repos/{repository}/contents/{path}"
            : $"/repos/{repository}/contents/{path}?ref={Uri.EscapeDataString(gitRef)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, $"GET {url}", cancellationToken);

        var json = await response.Content.ReadFromJsonAsync<FileContentJson>(JsonOptions, cancellationToken);
        if (json is null || json.Encoding != "base64") return null;

        var bytes = Convert.FromBase64String(json.Content.Replace("\n", ""));
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public async Task<IReadOnlyList<OrgIssueSearchHit>> SearchOpenOrgIssuesByLabelAsync(
        string organisation, string label, CancellationToken cancellationToken)
    {
        // archived:false — issues in archived repos are read-only: any run started for them
        // burns a full agent chain and then fails on the first comment post (403).
        var query = Uri.EscapeDataString($"org:{organisation} label:\"{label}\" is:issue is:open archived:false");
        var json  = await GetAsync<IssueSearchJson>($"/search/issues?q={query}&per_page=100", cancellationToken);
        return json.Items.Select(i => new OrgIssueSearchHit(
            RepositoryFromApiUrl(i.RepositoryUrl), i.Number, i.Title, i.Body, i.HtmlUrl,
            i.User.Login, i.Labels.Select(l => l.Name).ToArray(),
            i.UpdatedAt ?? i.CreatedAt)).ToArray();
    }

    // e.g. https://api.github.com/repos/yorrixx-apps/user-app-123 → yorrixx-apps/user-app-123
    private static string RepositoryFromApiUrl(string repositoryUrl)
    {
        const string marker = "/repos/";
        var idx = repositoryUrl.IndexOf(marker, StringComparison.Ordinal);
        return idx >= 0 ? repositoryUrl[(idx + marker.Length)..] : repositoryUrl;
    }

    // GitHub puts the actionable detail ("Requires authentication", "Validation Failed" with
    // field errors, rate-limit messages) in the response body — surface it, or failures show
    // up in audit/logs as a bare status code.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string context, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body   = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = body.Length > 500 ? body[..500] : body;
        throw new HttpRequestException(
            $"GitHub API returned {(int)response.StatusCode} ({response.StatusCode}) for {context}: {detail}",
            inner: null,
            statusCode: response.StatusCode);
    }

    public async Task<CreatedRepository> CreateRepositoryFromTemplateAsync(
        string templateRepository, string targetOwner, string name, bool isPrivate, string description,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            $"/repos/{templateRepository}/generate",
            new { owner = targetOwner, name, @private = isPrivate, description },
            JsonOptions, cancellationToken);

        // Ensure semantics (G6 P3): GitHub 422s "Name already exists" when a cold-start-lost create-build
        // is re-kicked. The repo from the earlier attempt IS the desired state — fetch and return it. If
        // the 422 was some other validation error, the GET 404s and surfaces that instead.
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var existing = await GetAsync<GeneratedRepoJson>($"/repos/{targetOwner}/{name}", cancellationToken);
            return new CreatedRepository(existing.FullName, existing.HtmlUrl, existing.DefaultBranch, existing.Owner?.Id ?? 0, existing.Id);
        }

        await EnsureSuccessAsync(response, $"POST /repos/{templateRepository}/generate", cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<GeneratedRepoJson>(JsonOptions, cancellationToken)
                   ?? throw new InvalidOperationException($"Empty response from GitHub API: POST /repos/{templateRepository}/generate");
        return new CreatedRepository(json.FullName, json.HtmlUrl, json.DefaultBranch, json.Owner?.Id ?? 0, json.Id);
    }

    public async Task<GitHubIssueReference> CreateIssueAsync(
        string repository, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var json = await PostAsync<CreatedIssueJson>(
            $"/repos/{repository}/issues",
            new { title, body, labels },
            cancellationToken);
        return new GitHubIssueReference(repository, json.Number, json.HtmlUrl);
    }

    private sealed record CreatedIssueJson(int Number, string HtmlUrl);

    public async Task SetRepoVariableAsync(string repository, string name, string value, CancellationToken cancellationToken)
    {
        // POST creates; if it already exists GitHub returns 409, so fall back to PATCH (idempotent set).
        using var create = await _http.PostAsJsonAsync(
            $"/repos/{repository}/actions/variables", new { name, value }, JsonOptions, cancellationToken);
        if (create.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            using var update = await _http.PatchAsJsonAsync(
                $"/repos/{repository}/actions/variables/{name}", new { name, value }, JsonOptions, cancellationToken);
            await EnsureSuccessAsync(update, $"PATCH variable {name}", cancellationToken);
            return;
        }
        await EnsureSuccessAsync(create, $"POST variable {name}", cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, $"GET {path}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException($"Empty response from GitHub API: GET {path}");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, $"POST {path}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException($"Empty response from GitHub API: POST {path}");
    }

    private static IssueDetails MapIssue(string repository, IssueJson j) => new()
    {
        Issue         = new GitHubIssueReference(repository, j.Number, j.HtmlUrl),
        Title         = j.Title,
        BodyMarkdown  = j.Body ?? string.Empty,
        State         = j.State,
        AuthorLogin   = j.User.Login,
        Labels        = j.Labels.Select(l => l.Name).ToArray(),
        CreatedAtUtc  = j.CreatedAt,
        UpdatedAtUtc  = j.UpdatedAt
    };

    private static IssueComment MapComment(string repository, int number, CommentJson c) => new()
    {
        CommentId                 = c.Id,
        Repository                = repository,
        IssueOrPullRequestNumber  = number,
        BodyMarkdown              = c.Body,
        AuthorLogin               = c.User.Login,
        Url                       = c.HtmlUrl,
        CreatedAtUtc              = c.CreatedAt,
        UpdatedAtUtc              = c.UpdatedAt
    };

    private static PullRequestDetails MapPullRequest(string repository, PullRequestJson j) => new()
    {
        PullRequest  = new GitHubPullRequestReference(repository, j.Number, j.Head.Ref, j.HtmlUrl),
        Title        = j.Title,
        BodyMarkdown = j.Body ?? string.Empty,
        State        = j.State,
        BaseBranch   = j.Base.Ref,
        HeadBranch   = j.Head.Ref,
        AuthorLogin  = j.User.Login,
        Draft        = j.Draft,
        Mergeable    = j.Mergeable ?? false,
        Labels       = j.Labels.Select(l => l.Name).ToArray(),
        CreatedAtUtc = j.CreatedAt,
        UpdatedAtUtc = j.UpdatedAt
    };

    // Internal JSON models — named to match GitHub API snake_case via JsonOptions
    private sealed record IssueJson(
        int Number, string Title, string? Body, string State,
        UserJson User, LabelJson[] Labels, string HtmlUrl,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record CommentJson(
        long Id, string Body, UserJson User, string HtmlUrl,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record PullRequestJson(
        int Number, string Title, string? Body, string State,
        BranchRefJson Head, BranchRefJson Base, UserJson User,
        bool Draft, bool? Mergeable, LabelJson[] Labels,
        string HtmlUrl, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record ChangedFileJson(
        string Filename, string Status, int Additions, int Deletions, int Changes);

    private sealed record CheckRunsJson(CheckRunJson[] CheckRuns);
    private sealed record CheckRunJson(
        long Id, string Name, string Status, string? Conclusion, string? DetailsUrl);

    private sealed record AnnotationJson(
        string Path, int StartLine, string AnnotationLevel, string Message);

    private sealed record TreeJson(TreeEntryJson[] Tree);
    private sealed record TreeEntryJson(string Path, string Type, long? Size);

    private sealed record IssueSearchJson(IssueSearchItemJson[] Items);
    private sealed record IssueSearchItemJson(
        int Number, string Title, string? Body, UserJson User, LabelJson[] Labels,
        string HtmlUrl, string RepositoryUrl, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record UserJson(string Login);
    private sealed record LabelJson(string Name);
    private sealed record BranchRefJson(string Ref, string? Sha = null);
    private sealed record FileContentJson(string Encoding, string Content, string? Sha);
    private sealed record GitRefJson(GitRefObjectJson Object);
    private sealed record GitRefObjectJson(string Sha);
    private sealed record RepositoryJson(string DefaultBranch);
    // Id + Owner.Id feed the IMMUTABLE OIDC subject (F5): GitHub's token sub claim now embeds them
    // (repo:{owner}@{ownerId}/{repo}@{repoId}:ref:...), and the fed cred must match exactly.
    private sealed record GeneratedRepoJson(string FullName, string HtmlUrl, string DefaultBranch, long Id, RepoOwnerJson? Owner);
    private sealed record RepoOwnerJson(long Id);
}
