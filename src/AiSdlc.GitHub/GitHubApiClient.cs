using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        var json = await PostAsync<PullRequestJson>(
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
            Name       = cr.Name,
            Status     = cr.Status,
            Conclusion = cr.Conclusion ?? "pending",
            DetailsUrl = cr.DetailsUrl
        }).ToArray();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException($"Empty response from GitHub API: GET {path}");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
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
        string Name, string Status, string? Conclusion, string? DetailsUrl);

    private sealed record UserJson(string Login);
    private sealed record LabelJson(string Name);
    private sealed record BranchRefJson(string Ref);
}
