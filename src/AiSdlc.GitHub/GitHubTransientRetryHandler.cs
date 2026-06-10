using System.Net;

namespace AiSdlc.GitHub;

/// <summary>
/// Retries transient GitHub API failures at the HTTP layer so every consumer of the typed
/// client (orchestrator activities, webhook processor, reconciliation sweep) is covered.
/// 401 is deliberately included: on 2026-06-10 GitHub intermittently returned
/// 401 "Requires authentication" for valid, never-rotated tokens (reproduced with two
/// unrelated tokens while githubstatus.com showed all-operational) — a one-off 401 killed
/// a whole orchestration. Retrying on a response status is safe for non-idempotent
/// requests: the server rejected the call, so nothing was processed.
/// </summary>
public sealed class GitHubTransientRetryHandler : DelegatingHandler
{
    private static readonly TimeSpan[] DefaultDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20)];

    private readonly TimeSpan[] _delays;

    public GitHubTransientRetryHandler() : this(DefaultDelays) { }

    // Test seam — lets unit tests use zero delays.
    public GitHubTransientRetryHandler(TimeSpan[] delays)
    {
        _delays = delays;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the content up front so the request can be re-sent.
        var contentBytes = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var response = await base.SendAsync(await CloneAsync(request, contentBytes), cancellationToken);

        foreach (var delay in _delays)
        {
            if (!IsTransient(response.StatusCode))
                return response;

            response.Dispose();
            await Task.Delay(delay, cancellationToken);
            response = await base.SendAsync(await CloneAsync(request, contentBytes), cancellationToken);
        }

        return response;
    }

    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, byte[]? contentBytes)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version       = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var (key, values) in request.Headers)
            clone.Headers.TryAddWithoutValidation(key, values);

        if (contentBytes is not null && request.Content is not null)
        {
            var content = new ByteArrayContent(contentBytes);
            foreach (var (key, values) in request.Content.Headers)
                content.Headers.TryAddWithoutValidation(key, values);
            clone.Content = content;
        }

        return Task.FromResult(clone);
    }
}
