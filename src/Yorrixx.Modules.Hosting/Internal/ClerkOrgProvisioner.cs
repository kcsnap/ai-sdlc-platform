using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yorrixx.Contracts.Hosting;

namespace Yorrixx.Modules.Hosting.Internal;

/// Real `IClerkOrgProvisioner` against Clerk's Backend API. Single-instance
/// multi-Org model: every user-app gets its own Clerk Organization within
/// the Yorrixx-owned instance, sharing one instance publishable key.
///
/// Idempotent: lookup-by-slug returns the existing Org if a previous
/// Go-Live succeeded; otherwise create + add builder as `org:admin`.
internal sealed class ClerkOrgProvisioner : IClerkOrgProvisioner
{
    private const string BackendBaseUrl = "https://api.clerk.com/v1/";

    private readonly HttpClient _http;
    private readonly HostingOptions _opts;
    private readonly ILogger<ClerkOrgProvisioner> _logger;

    public ClerkOrgProvisioner(
        HttpClient http,
        IOptions<HostingOptions> options,
        ILogger<ClerkOrgProvisioner> logger)
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(BackendBaseUrl);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _opts.ClerkSecretKey);
    }

    public async Task<UserAppClerkOrg> EnsureAsync(
        string appId,
        string appName,
        string builderUserId,
        CancellationToken cancellationToken = default)
    {
        var slug = $"app-{NormaliseSlug(appId)}";
        var publishableKey = _opts.ClerkPublishableKeyFallback;

        var existing = await TryGetBySlugAsync(slug, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("clerk org already exists slug={Slug} orgId={OrgId}", slug, existing.Id);
            return new UserAppClerkOrg(appId, existing.Id, publishableKey, existing.CreatedAtUtc);
        }

        var createReq = new CreateOrganizationRequest(
            Name: appName,
            Slug: slug,
            CreatedBy: string.IsNullOrWhiteSpace(builderUserId) ? null : builderUserId);

        using var resp = await _http.PostAsJsonAsync("organizations", createReq, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("clerk createOrganization failed status={Status} slug={Slug} body={Body}",
                resp.StatusCode, slug, errorBody);
            throw new InvalidOperationException(
                $"Clerk createOrganization failed ({(int)resp.StatusCode}): {errorBody}");
        }

        var created = await resp.Content.ReadFromJsonAsync<OrganizationResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Clerk createOrganization returned empty body");

        _logger.LogInformation("created clerk org slug={Slug} orgId={OrgId} builder={Builder}",
            slug, created.Id, builderUserId);

        return new UserAppClerkOrg(appId, created.Id, publishableKey, created.CreatedAtUtc);
    }

    public async Task RemoveAsync(string appId, CancellationToken cancellationToken = default)
    {
        var slug = $"app-{NormaliseSlug(appId)}";
        var existing = await TryGetBySlugAsync(slug, cancellationToken);
        if (existing is null)
        {
            _logger.LogDebug("clerk org remove skipped — not found slug={Slug}", slug);
            return;
        }

        using var resp = await _http.DeleteAsync($"organizations/{existing.Id}", cancellationToken);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("clerk deleteOrganization failed status={Status} orgId={OrgId} body={Body}",
                resp.StatusCode, existing.Id, body);
            throw new InvalidOperationException(
                $"Clerk deleteOrganization failed ({(int)resp.StatusCode}): {body}");
        }
        _logger.LogInformation("deleted clerk org slug={Slug} orgId={OrgId}", slug, existing.Id);
    }

    public async Task<int> CleanupE2eTestUsersAsync(string appId, CancellationToken cancellationToken = default)
    {
        // The seeded auth spec registers users as e2e-{appId8}-{ts}+clerk_test@…
        // — the prefix scopes the query to this app; the +clerk_test guard
        // makes accidental deletion of a real user structurally impossible.
        var prefix = $"e2e-{NormaliseSlug(appId)}-";

        using var resp = await _http.GetAsync(
            $"users?query={Uri.EscapeDataString(prefix)}&limit=100", cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("clerk e2e cleanup: user query failed status={Status}", resp.StatusCode);
            return 0;
        }

        var users = await resp.Content.ReadFromJsonAsync<List<ClerkUser>>(cancellationToken)
            ?? [];

        var deleted = 0;
        foreach (var user in users)
        {
            var emails = user.EmailAddresses?.Select(e => e.EmailAddress) ?? [];
            if (!emails.Any(e => e is not null &&
                    e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    e.Contains("+clerk_test", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            using var del = await _http.DeleteAsync($"users/{user.Id}", cancellationToken);
            if (del.IsSuccessStatusCode)
            {
                deleted++;
            }
            else
            {
                _logger.LogWarning("clerk e2e cleanup: delete failed userId={UserId} status={Status}",
                    user.Id, del.StatusCode);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("clerk e2e cleanup: deleted {Count} test user(s) appId={AppId}", deleted, appId);
        }
        return deleted;
    }

    private sealed record ClerkUser(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("email_addresses")] List<ClerkEmail>? EmailAddresses);

    private sealed record ClerkEmail(
        [property: JsonPropertyName("email_address")] string? EmailAddress);

    private async Task<OrganizationResponse?> TryGetBySlugAsync(string slug, CancellationToken ct)
    {
        // Clerk Backend API: GET /v1/organizations?query={slug} returns a list.
        // We match exactly on slug — `query` is a substring match by default.
        using var resp = await _http.GetAsync($"organizations?query={Uri.EscapeDataString(slug)}&limit=10", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("clerk listOrganizations failed status={Status} body={Body}",
                resp.StatusCode, body);
            return null;
        }

        var page = await resp.Content.ReadFromJsonAsync<OrganizationListResponse>(ct);
        return page?.Data?.FirstOrDefault(o => string.Equals(o.Slug, slug, StringComparison.Ordinal));
    }

    private static string NormaliseSlug(string appId) =>
        appId.Replace("-", "").ToLowerInvariant() is var clean && clean.Length >= 8 ? clean[..8] : clean;

    private sealed record CreateOrganizationRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("slug")] string Slug,
        // Optional: omitted (null) when the caller has no real Clerk user for the owner — Clerk 400s
        // organization_creator_not_found on any id that isn't a real user, so no-creator beats placeholder.
        [property: JsonPropertyName("created_by"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedBy);

    private sealed record OrganizationResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("created_at")] long CreatedAtMs)
    {
        public DateTimeOffset CreatedAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs);
    }

    private sealed record OrganizationListResponse(
        [property: JsonPropertyName("data")] List<OrganizationResponse>? Data);
}
