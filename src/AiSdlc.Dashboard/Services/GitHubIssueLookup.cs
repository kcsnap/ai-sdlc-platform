using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Dashboard.Services;

public sealed record GitHubIssueInfo(string Title, string State, string? StateReason);

public interface IGitHubIssueLookup
{
    Task<GitHubIssueInfo?> GetIssueAsync(string repository, int issueNumber, CancellationToken cancellationToken);
}

// Best-effort fetch of issue title + state from the GitHub REST API.
// Used by the dashboard to populate fields that aren't yet in audit (e.g. for runs created before
// the orchestrator started recording issueTitle/issueState in References).
//
// Falls back to returning null when:
//   - No GitHub PAT is configured (env var GitHubPat)
//   - GitHub returns 404 / 403 / other non-success
// Caching is in-memory and process-lifetime — refreshed on each dashboard restart.
public sealed class GitHubIssueLookup : IGitHubIssueLookup
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubIssueLookup> _logger;
    private readonly ConcurrentDictionary<string, Task<GitHubIssueInfo?>> _cache = new(StringComparer.Ordinal);
    private readonly bool _enabled;

    public GitHubIssueLookup(HttpClient http, ILogger<GitHubIssueLookup> logger)
    {
        _http   = http;
        _logger = logger;
        _enabled = _http.DefaultRequestHeaders.Authorization is not null;
        if (!_enabled)
        {
            _logger.LogWarning("GitHubIssueLookup is disabled: no GitHubPat env var configured. Titles will not be backfilled.");
        }
    }

    public Task<GitHubIssueInfo?> GetIssueAsync(string repository, int issueNumber, CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return Task.FromResult<GitHubIssueInfo?>(null);
        }

        var key = $"{repository}#{issueNumber}";
        return _cache.GetOrAdd(key, _ => FetchAsync(repository, issueNumber, cancellationToken));
    }

    private async Task<GitHubIssueInfo?> FetchAsync(string repository, int issueNumber, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync($"repos/{repository}/issues/{issueNumber}", cancellationToken);
            // 404 = never existed, 410 = deleted. Both are "no data" rather than errors.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                _logger.LogInformation("Issue {Repo}#{Issue} not available on GitHub ({Status}).", repository, issueNumber, response.StatusCode);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<IssuePayload>(cancellationToken: cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Title))
            {
                return null;
            }

            return new GitHubIssueInfo(
                Title:       payload.Title,
                State:       payload.State ?? "open",
                StateReason: payload.StateReason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch issue {Repo}#{Issue} from GitHub.", repository, issueNumber);
            return null;
        }
    }

    private sealed record IssuePayload
    {
        [JsonPropertyName("title")]        public string Title { get; init; } = string.Empty;
        [JsonPropertyName("state")]        public string? State { get; init; }
        [JsonPropertyName("state_reason")] public string? StateReason { get; init; }
    }
}
