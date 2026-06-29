using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yorrixx.Contracts.Hosting;

namespace Yorrixx.Modules.Hosting.Internal;

/// Real IUserAppDeployIdentityProvisioner backed by Microsoft Graph. Creates
/// one Entra application registration + service principal per user-app,
/// with a federated identity credential bound to that user-app's GitHub
/// repo + default branch. The repo's `deploy.yml` then uses
/// `azure/login@v2` with this SP via OIDC — no long-lived secrets.
///
/// Idempotent: re-running EnsureAsync for the same appId+repo returns the
/// existing application + SP + federated credential rather than creating
/// duplicates.
///
/// Requires the API's managed identity to have
/// `Application.ReadWrite.OwnedBy` on Microsoft Graph
/// (see `STAGE_1_SETUP.md` §2).
internal sealed class EntraDeployIdentityProvisioner : IUserAppDeployIdentityProvisioner
{
    private const string GraphV1   = "https://graph.microsoft.com/v1.0";
    private const string GraphScope = "https://graph.microsoft.com/.default";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly HostingOptions _opts;
    private readonly ILogger<EntraDeployIdentityProvisioner> _logger;

    public EntraDeployIdentityProvisioner(
        HttpClient httpClient,
        TokenCredential credential,
        IOptions<HostingOptions> options,
        ILogger<EntraDeployIdentityProvisioner> logger)
    {
        _http       = httpClient;
        _credential = credential;
        _opts       = options.Value;
        _logger     = logger;
    }

    public async Task<UserAppDeployIdentity> EnsureAsync(
        string appId,
        string repoOwner,
        string repoName,
        string defaultBranch,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthHeaderAsync(cancellationToken);

        var id8         = ResourceNames.From(appId, appName: "").Id8;
        var displayName = $"sp-userapp-{id8}";

        var application = await GetOrCreateApplicationAsync(displayName, cancellationToken);
        var sp          = await GetOrCreateServicePrincipalAsync(application.AppId, cancellationToken);
        await EnsureFederatedCredentialAsync(application.Id, repoOwner, repoName, defaultBranch, cancellationToken);

        _logger.LogInformation(
            "deploy SP ensured appId={AppId} displayName={Name} appReg={AppRegId} sp={SpId}",
            appId, displayName, application.Id, sp.Id);

        return new UserAppDeployIdentity(
            AppId:                   appId,
            TenantId:                _opts.TenantId,
            SubscriptionId:          _opts.SubscriptionId,
            ClientId:                application.AppId,
            ApplicationObjectId:     application.Id,
            ServicePrincipalObjectId: sp.Id,
            CreatedAt:               DateTimeOffset.UtcNow);
    }

    public async Task RemoveAsync(string appId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthHeaderAsync(cancellationToken);

        var id8         = ResourceNames.From(appId, appName: "").Id8;
        var displayName = $"sp-userapp-{id8}";

        var app = await TryGetApplicationByDisplayNameAsync(displayName, cancellationToken);
        if (app is null)
        {
            _logger.LogDebug("deploy SP remove: not found displayName={Name}", displayName);
            return;
        }

        // Deleting the application reg cascades to its SP and federated
        // creds, so no separate cleanup needed.
        var resp = await _http.DeleteAsync($"{GraphV1}/applications/{app.Id}", cancellationToken);
        await EnsureSuccessAsync(resp, $"delete application {app.Id}", cancellationToken);

        _logger.LogInformation("deploy SP removed displayName={Name} appReg={Id}", displayName, app.Id);
    }

    // ------------------------------------------------------------------
    // Graph operations
    // ------------------------------------------------------------------

    private async Task<GraphApplication> GetOrCreateApplicationAsync(string displayName, CancellationToken ct)
    {
        var existing = await TryGetApplicationByDisplayNameAsync(displayName, ct);
        if (existing is not null)
        {
            _logger.LogDebug("application exists displayName={Name} id={Id}", displayName, existing.Id);
            // Defensive: make sure the API MI is an owner even on found
            // apps (older runs may have created the App before this code
            // existed, leaving it ownerless from the MI's perspective).
            await EnsureApiMiIsOwnerAsync(existing.Id, ct);
            return existing;
        }

        var payload = new GraphApplicationCreate
        {
            DisplayName    = displayName,
            // Single-tenant. The federated credential restricts which GH
            // repo+ref can mint tokens, so multi-tenant audience would just
            // increase blast radius without buying anything.
            SignInAudience = "AzureADMyOrg",
        };
        var resp = await _http.PostAsJsonAsync($"{GraphV1}/applications", payload, JsonOpts, ct);
        await EnsureSuccessAsync(resp, $"create application '{displayName}'", ct);
        var created = await resp.Content.ReadFromJsonAsync<GraphApplication>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Empty response from create application.");

        // Add the API MI as an owner so the *next* call (create
        // servicePrincipal) is allowed under `Application.ReadWrite.OwnedBy`.
        // Graph does NOT auto-add the creator as owner when a non-user SP
        // mints the App; the call returns 403 Authorization_RequestDenied
        // on the SP create otherwise.
        await EnsureApiMiIsOwnerAsync(created.Id, ct);

        return created;
    }

    private async Task EnsureApiMiIsOwnerAsync(string appObjectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiManagedIdentityClientId))
        {
            _logger.LogWarning(
                "Hosting:ApiManagedIdentityClientId not configured — skipping owner add on application {AppObjectId}. " +
                "Subsequent servicePrincipal create will likely 403.", appObjectId);
            return;
        }

        // Resolve our own SP object id from our MI client id.
        var apiSp = await TryFindServicePrincipalAsync(_opts.ApiManagedIdentityClientId, ct);
        if (apiSp is null)
        {
            _logger.LogWarning(
                "API MI servicePrincipal not found for clientId={ClientId} — skipping owner add on application {AppObjectId}.",
                _opts.ApiManagedIdentityClientId, appObjectId);
            return;
        }

        // POST /applications/{id}/owners/$ref with the SP's directory object ref.
        // Retry on 404 to absorb Graph's eventual-consistency propagation
        // window — a brand-new application object can take a few seconds
        // to show up across all Graph replicas, so the immediately-following
        // /owners/$ref call sometimes 404s the very id we just created.
        // ~30 s total wait window (6 × 5 s) before giving up.
        var jsonBody = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["@odata.id"] = $"{GraphV1}/directoryObjects/{apiSp.Id}",
        }, JsonOpts);

        const int maxAttempts = 6;
        var delay = TimeSpan.FromSeconds(5);
        HttpResponseMessage? resp = null;
        string bodyText = "";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            resp = await _http.PostAsync(
                $"{GraphV1}/applications/{appObjectId}/owners/$ref", content, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogInformation(
                    "API MI added as owner of application {AppObjectId} spId={SpId} attempt={Attempt}",
                    appObjectId, apiSp.Id, attempt);
                return;
            }

            bodyText = await resp.Content.ReadAsStringAsync(ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                bodyText.Contains("already exist", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("API MI already an owner of application {AppObjectId}", appObjectId);
                return;
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound && attempt < maxAttempts)
            {
                _logger.LogInformation(
                    "Graph add-owner 404 on application {AppObjectId} (attempt {Attempt}/{Max}) — propagation race, retrying in {DelaySec}s",
                    appObjectId, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            // Non-retryable status, or final 404 after exhausting attempts.
            break;
        }

        throw new InvalidOperationException(
            $"Graph add owner to application {appObjectId} failed " +
            $"({(int)(resp?.StatusCode ?? 0)}): {bodyText}");
    }

    private async Task<GraphApplication?> TryGetApplicationByDisplayNameAsync(string displayName, CancellationToken ct)
    {
        var url  = $"{GraphV1}/applications?$filter=displayName eq '{Uri.EscapeDataString(displayName)}'";
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(resp, $"list applications displayName={displayName}", ct);
        var list = await resp.Content.ReadFromJsonAsync<GraphListResponse<GraphApplication>>(JsonOpts, ct);
        return list?.Value.FirstOrDefault();
    }

    private async Task<GraphServicePrincipal> GetOrCreateServicePrincipalAsync(string appClientId, CancellationToken ct)
    {
        // 1. Look up first. The $filter on appId is the standard Graph pattern,
        //    but Graph's search index is eventually consistent — a SP created
        //    seconds ago may not yet be visible through this filter.
        var existing = await TryFindServicePrincipalAsync(appClientId, ct);
        if (existing is not null)
        {
            _logger.LogDebug("servicePrincipal exists appId={AppId} id={Id}", appClientId, existing.Id);
            return existing;
        }

        // 2. Create. Three known races on the POST:
        //    a) 409 ObjectConflict — a previous attempt's SP exists but the
        //       $filter above missed it; re-look-up and use it.
        //    b) 400 NoBackingApplicationObject — application was created
        //       seconds ago and hasn't propagated to all Graph replicas yet,
        //       so the SP-create can't resolve the appId. Retry the POST
        //       with a 5 s backoff up to 6 times (30 s total window) before
        //       giving up.
        //    c) 403 "…backing application…must in the local tenant" — same
        //       propagation race as (b) seen through the
        //       Application.ReadWrite.OwnedBy permission check: the replica
        //       can't see the application (or its just-added owner edge), so
        //       Graph reports it as not-in-tenant instead of not-found. A
        //       *real* permission problem returns a different 403 message
        //       ("Insufficient privileges…"), which still fails immediately.
        var payload = new { appId = appClientId };

        const int maxAttempts = 6;
        var delay = TimeSpan.FromSeconds(5);
        HttpResponseMessage? resp = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            resp = await _http.PostAsJsonAsync($"{GraphV1}/servicePrincipals", payload, JsonOpts, ct);

            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadFromJsonAsync<GraphServicePrincipal>(JsonOpts, ct)
                    ?? throw new InvalidOperationException("Empty response from create servicePrincipal.");
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The SP exists server-side but the $filter index is
                // eventually consistent — a single immediate re-lookup can
                // miss it (killed v002's first provisioning attempt, the 7th
                // Graph race). Keep looking with the same ~30 s window as
                // the other races.
                _logger.LogInformation(
                    "servicePrincipal create returned 409 — SP already exists for appId={AppId}; polling lookup", appClientId);
                for (var lookup = 1; lookup <= maxAttempts; lookup++)
                {
                    var retried = await TryFindServicePrincipalAsync(appClientId, ct);
                    if (retried is not null) return retried;
                    if (lookup < maxAttempts) await Task.Delay(delay, ct);
                }
                // 409 with no findable SP after the full window — surface it.
                break;
            }

            if (resp.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden
                && attempt < maxAttempts)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (body.Contains("NoBackingApplicationObject", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("must in the local tenant", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "servicePrincipal create {Status} (propagation race) for appId={AppId} (attempt {Attempt}/{Max}) — retrying in {DelaySec}s",
                        (int)resp.StatusCode, appClientId, attempt, maxAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    continue;
                }
            }

            // Anything else: surface immediately via EnsureSuccessAsync below.
            break;
        }

        await EnsureSuccessAsync(resp!, $"create servicePrincipal appId={appClientId}", ct);
        // EnsureSuccessAsync throws on failure, so this is unreachable on
        // non-2xx; included only to satisfy the compiler.
        return await resp!.Content.ReadFromJsonAsync<GraphServicePrincipal>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Empty response from create servicePrincipal.");
    }

    private async Task<GraphServicePrincipal?> TryFindServicePrincipalAsync(string appClientId, CancellationToken ct)
    {
        var listUrl = $"{GraphV1}/servicePrincipals?$filter=appId eq '{appClientId}'";
        var listResp = await _http.GetAsync(listUrl, ct);
        await EnsureSuccessAsync(listResp, $"list servicePrincipals appId={appClientId}", ct);
        var list = await listResp.Content.ReadFromJsonAsync<GraphListResponse<GraphServicePrincipal>>(JsonOpts, ct);
        return list?.Value.FirstOrDefault();
    }

    private async Task EnsureFederatedCredentialAsync(
        string appObjectId, string repoOwner, string repoName, string branch, CancellationToken ct)
    {
        var subject = $"repo:{repoOwner}/{repoName}:ref:refs/heads/{branch}";
        var name    = SanitiseFedCredName($"gh-{repoName}-{branch}");

        var listResp = await _http.GetAsync(
            $"{GraphV1}/applications/{appObjectId}/federatedIdentityCredentials", ct);
        // 404 on the list is the application-propagation race (the replica
        // can't see the just-created application yet) — treat as "no
        // credentials" and let the create loop below absorb the wait.
        if (listResp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(listResp, $"list federatedIdentityCredentials appObj={appObjectId}", ct);
            var list = await listResp.Content.ReadFromJsonAsync<GraphListResponse<GraphFederatedCredential>>(JsonOpts, ct);
            if (list?.Value.Any(c => c.Subject == subject) == true)
            {
                _logger.LogDebug("federatedIdentityCredential exists subject={Subject}", subject);
                return;
            }
        }

        var payload = new GraphFederatedCredentialCreate
        {
            Name      = name,
            Issuer    = "https://token.actions.githubusercontent.com",
            Subject   = subject,
            Audiences = new[] { "api://AzureADTokenExchange" },
        };

        const int maxAttempts = 6;
        var delay = TimeSpan.FromSeconds(5);
        HttpResponseMessage? resp = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            resp = await _http.PostAsJsonAsync(
                $"{GraphV1}/applications/{appObjectId}/federatedIdentityCredentials", payload, JsonOpts, ct);

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("federatedIdentityCredential created subject={Subject} name={Name}", subject, name);
                return;
            }

            // Graph's list-then-create dance has a propagation race: the list
            // call can return stale data that omits a credential created in a
            // very recent run, so we POST blind and Graph rejects with 409
            // `Request_MultipleObjectsWithSameKeyValue`. The credential name
            // is deterministic (gh-{repo}-{branch}) so a same-name conflict
            // means our previous attempt already created what we want —
            // idempotent success.
            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var conflictBody = await resp.Content.ReadAsStringAsync(ct);
                if (conflictBody.Contains("MultipleObjectsWithSameKeyValue", StringComparison.OrdinalIgnoreCase) ||
                    conflictBody.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "federatedIdentityCredential already exists (409 on POST, stale list) subject={Subject} name={Name}",
                        subject, name);
                    return;
                }
                break;
            }

            // Same duplicate, different shape: a subject+issuer collision
            // surfaces as 400 "Request contains a property with duplicate
            // values" rather than the 409 name conflict. Seen 2026-06-11
            // (user-app-7301476a): a POST that "failed" with the propagation
            // 404 had actually persisted, so this loop's own retry — and any
            // later re-run whose list read was stale — duplicates it. Name
            // and subject are both deterministic, so the existing credential
            // is necessarily the one we want — idempotent success.
            if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var badReqBody = await resp.Content.ReadAsStringAsync(ct);
                if (badReqBody.Contains("duplicate values", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "federatedIdentityCredential already exists (400 duplicate on POST) subject={Subject} name={Name}",
                        subject, name);
                    return;
                }
                break;
            }

            // 404 Request_ResourceNotFound — the application object hasn't
            // propagated to the replica handling this POST yet. Same race
            // as add-owner (404) and SP-create (400/403); same ~30 s
            // window (6 × 5 s).
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound && attempt < maxAttempts)
            {
                _logger.LogInformation(
                    "federatedIdentityCredential create 404 on appObj={AppObjectId} (attempt {Attempt}/{Max}) — propagation race, retrying in {DelaySec}s",
                    appObjectId, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            break;
        }

        await EnsureSuccessAsync(resp!, $"create federatedIdentityCredential subject={subject}", ct);
        _logger.LogInformation("federatedIdentityCredential created subject={Subject} name={Name}", subject, name);
    }

    // Federated credential names must be 3-120 chars, alphanumeric / dash /
    // underscore. Other characters are replaced with dashes; oversized
    // names are truncated.
    private static string SanitiseFedCredName(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('-');
            }
        }
        return sb.Length > 120 ? sb.ToString(0, 120) : sb.ToString();
    }

    private async Task EnsureAuthHeaderAsync(CancellationToken ct)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(new[] { GraphScope }), ct);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string action, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Graph {action} failed ({(int)resp.StatusCode} {resp.ReasonPhrase}): {body}");
    }

    // ------------------------------------------------------------------
    // Graph response / request DTOs
    // ------------------------------------------------------------------

    private sealed class GraphListResponse<T>
    {
        public IReadOnlyList<T> Value { get; init; } = Array.Empty<T>();
    }

    private sealed class GraphApplication
    {
        public string Id          { get; init; } = "";  // object id
        public string AppId       { get; init; } = "";  // client id
        public string DisplayName { get; init; } = "";
    }

    private sealed class GraphApplicationCreate
    {
        public string DisplayName    { get; init; } = "";
        public string SignInAudience { get; init; } = "";
    }

    private sealed class GraphServicePrincipal
    {
        public string Id    { get; init; } = "";  // object id (RBAC principal id)
        public string AppId { get; init; } = "";
    }

    private sealed class GraphFederatedCredential
    {
        public string Id        { get; init; } = "";
        public string Name      { get; init; } = "";
        public string Issuer    { get; init; } = "";
        public string Subject   { get; init; } = "";
        public IReadOnlyList<string> Audiences { get; init; } = Array.Empty<string>();
    }

    private sealed class GraphFederatedCredentialCreate
    {
        public string Name      { get; init; } = "";
        public string Issuer    { get; init; } = "";
        public string Subject   { get; init; } = "";
        public IReadOnlyList<string> Audiences { get; init; } = Array.Empty<string>();
    }
}
