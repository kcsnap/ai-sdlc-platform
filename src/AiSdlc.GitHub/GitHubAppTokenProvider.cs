using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiSdlc.GitHub;

/// <summary>
/// Mints and caches GitHub App installation access tokens. The platform was PAT-based; this is the
/// lift-and-shift onto the existing GitHub App (App id + private key supplied via config) so the platform
/// can act as the App — create repos, commit, comment. Builds the App JWT (RS256), resolves the org's
/// installation once, exchanges the JWT for an installation token, and caches it until shortly before
/// expiry. Thread-safe: concurrent callers share one in-flight refresh.
/// </summary>
public sealed class GitHubAppTokenProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // Refresh a little before the ~1h expiry so an in-flight call never races the boundary.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;   // base = https://api.github.com; App-level (JWT) auth set per call
    private readonly string _appId;
    private readonly string _privateKeyPem;
    private readonly string _org;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt;
    private long? _installationId;

    public GitHubAppTokenProvider(HttpClient http, string appId, string privateKeyPem, string org, TimeProvider time)
    {
        _http          = http;
        _appId         = appId;
        _privateKeyPem = privateKeyPem;
        _org           = org;
        _time          = time;
    }

    public async Task<string> GetInstallationTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && _time.GetUtcNow() < _expiresAt - RefreshSkew)
            return _cachedToken;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the lock — another caller may have refreshed while we waited.
            if (_cachedToken is not null && _time.GetUtcNow() < _expiresAt - RefreshSkew)
                return _cachedToken;

            var jwt            = BuildAppJwt(_time.GetUtcNow());
            var installationId = _installationId ??= await ResolveInstallationIdAsync(jwt, cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/app/installations/{installationId}/access_tokens");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<InstallationTokenJson>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Empty installation-token response from GitHub.");

            _cachedToken = token.Token;
            _expiresAt   = token.ExpiresAt;
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<long> ResolveInstallationIdAsync(string jwt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/app/installations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var installations = await response.Content.ReadFromJsonAsync<InstallationJson[]>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty installations response from GitHub.");

        // Prefer the named org; fall back to the sole installation when there's exactly one.
        var match = installations.FirstOrDefault(i => string.Equals(i.Account?.Login, _org, StringComparison.OrdinalIgnoreCase))
            ?? (installations.Length == 1 ? installations[0] : null)
            ?? throw new InvalidOperationException(
                $"GitHub App has no installation for org '{_org}' (found {installations.Length} installation(s)).");
        return match.Id;
    }

    private string BuildAppJwt(DateTimeOffset now)
    {
        // GitHub permits up to 10 min; use 9, backdated 30s for clock skew between us and GitHub.
        var iat = now.AddSeconds(-30).ToUnixTimeSeconds();
        var exp = now.AddMinutes(9).ToUnixTimeSeconds();

        var header  = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"JWT"}"""));
        var payload = Base64Url(Encoding.UTF8.GetBytes($$"""{"iat":{{iat}},"exp":{{exp}},"iss":"{{_appId}}"}"""));
        var signingInput = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_privateKeyPem);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record InstallationTokenJson(string Token, DateTimeOffset ExpiresAt);
    private sealed record InstallationJson(long Id, InstallationAccountJson? Account);
    private sealed record InstallationAccountJson(string Login);
}
