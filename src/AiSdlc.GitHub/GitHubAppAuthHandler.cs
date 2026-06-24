using System.Net.Http.Headers;

namespace AiSdlc.GitHub;

/// <summary>
/// Sets a fresh GitHub App installation token on every request. Installation tokens rotate ~hourly, so —
/// unlike the static PAT header set once at DI time — the Authorization header must be applied per request
/// from <see cref="GitHubAppTokenProvider"/> (which caches and refreshes the token underneath).
/// </summary>
public sealed class GitHubAppAuthHandler : DelegatingHandler
{
    private readonly GitHubAppTokenProvider _tokens;

    public GitHubAppAuthHandler(GitHubAppTokenProvider tokens) => _tokens = tokens;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetInstallationTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
