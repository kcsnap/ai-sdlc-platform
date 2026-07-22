using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ApplicationInsights;
using Azure.ResourceManager.ApplicationInsights.Models;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Authorization.Models;
using Azure.ResourceManager.CosmosDB;
using Azure.ResourceManager.CosmosDB.Models;
using Azure.ResourceManager.ManagedServiceIdentities;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yorrixx.Contracts.Hosting;

namespace Yorrixx.Modules.Hosting.Internal;

/// User-app provisioning per ADR-0002 (F1 frontend Web App) + ADR-0003 (Flex
/// Consumption API Function App). Each call to EnsureProvisionedAsync lands the
/// full per-app resource set into a single shared resource group:
///
///   * F1 App Service Plan (Linux) + frontend Web App (Node 22)
///   * Flex Consumption plan + Function App (.NET 9 isolated)
///   * General-purpose v2 storage account for the Function App runtime
///   * User-assigned MI assigned to both compute targets
///   * App Insights component attached to the shared Log Analytics workspace
///   * Cosmos SQL container in the shared serverless account (`/id` PK)
///   * Cosmos data-plane RBAC scoped to that container only
///   * Key Vault Secrets User on the shared vault (secret prefix enforced in
///     user-app code; vault-level is the smallest RBAC scope KV offers)
///   * Deploy SP + federated credential bound to the user-app repo's main
///     branch (via IUserAppDeployIdentityProvisioner)
///   * Clerk Organization for per-app sign-in branding
///
/// Idempotent across every resource: re-running for the same appId converges
/// to the same state without duplicate creates. Resource names are derived
/// deterministically from (appId, appName) — see ResourceNames.
internal sealed class HostingService : IHostingService
{
    private readonly ArmClient _arm;
    private readonly HostingOptions _opts;
    private readonly IClerkOrgProvisioner _clerk;
    private readonly IUserAppDeployIdentityProvisioner _deployIdentity;
    private readonly ILogger<HostingService> _logger;
    private readonly TokenCredential _credential;
    private readonly IHttpClientFactory _httpFactory;

    // Built-in Cosmos DB data-plane role — read/write on a container scope.
    private const string CosmosDataContributorRoleDefinitionId = "00000000-0000-0000-0000-000000000002";

    // Built-in ARM role — Key Vault Secrets User (data-plane reader).
    private const string KeyVaultSecretsUserRoleDefinitionId = "4633458b-17de-408a-b874-0445c86b69e6";

    // Built-in ARM role — Website Contributor. Covers
    // Microsoft.Web/sites/publish + sites/config/list, which is what
    // azure/webapps-deploy@v3 and azure/functions-action@v1 need.
    private const string WebsiteContributorRoleDefinitionId = "de139f84-1756-47ae-9be6-808fbbe84772";

    // Built-in ARM role — Reader. Needed at RG scope so the deploy SP's
    // `az account list` (called inside azure/login@v2) returns a non-empty
    // subscription list.
    private const string ReaderRoleDefinitionId = "acdd72a7-3385-48ef-bd42-f606fba81ae7";

    // Built-in ARM role — Contributor. Granted to the deploy SP on a Static Web
    // App scope: covers Microsoft.Web/staticSites/listSecrets/action (fetching
    // the deploy token at run time) + the SWA content deploy. Website Contributor
    // does NOT cover staticSites/listSecrets, so Static apps need this.
    private const string ContributorRoleDefinitionId = "b24988ac-6180-42a0-ab88-20f7382dd24c";

    // Built-in ARM role — Storage Blob Data Contributor. Granted to the deploy SP
    // on a Static-profile app's storage account so `az storage blob upload-batch
    // --auth-mode login` can publish the site to the `$web` static-website container.
    private const string StorageBlobDataContributorRoleDefinitionId = "ba92f5b4-2d11-453d-a403-e96b0029c9fe";

    public HostingService(
        ArmClient arm,
        IOptions<HostingOptions> options,
        IClerkOrgProvisioner clerk,
        IUserAppDeployIdentityProvisioner deployIdentity,
        ILogger<HostingService> logger,
        TokenCredential credential,
        IHttpClientFactory httpFactory)
    {
        _arm = arm;
        _opts = options.Value;
        _clerk = clerk;
        _deployIdentity = deployIdentity;
        _logger = logger;
        _credential = credential;
        _httpFactory = httpFactory;
    }

    public async Task<DeployedApp?> GetAsync(string appId, CancellationToken cancellationToken = default)
    {
        // We don't have the charter name here, so we can't re-derive the
        // {kind}-{slug8}-{id8} names directly — instead resolve by the
        // deterministic `-{id8}-frontend` suffix and reconstruct the slug
        // from the Web App that actually exists. (The previous guess-the-
        // slug-is-"app" approach 404'd for every real app and broke release
        // verification's "no hosting record" path — v002, 2026-06-12.)
        var id8 = ResourceNames.From(appId, appName: "").Id8;
        var suffix = $"-{id8}-frontend";
        var rg = ResourceGroup();

        await foreach (var site in rg.GetWebSites().GetAllAsync(cancellationToken: cancellationToken))
        {
            var name = site.Data.Name;
            if (!name.StartsWith("app-", StringComparison.Ordinal) ||
                !name.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var slug8 = name["app-".Length..^suffix.Length];
            var names = new ResourceNames(slug8, id8);
            return SnapshotFrom(site, names, appId, ownerUserId: "");
        }

        // Static profile: no F1 Web App exists — the frontend is an Azure Storage
        // static website (storage account `st{id8}` with `$web` enabled). The
        // account name is deterministic, and its web endpoint is only populated
        // once static website is on — which disambiguates it from a plain/full-
        // stack storage account (Storage-static-website migration, 2026-06-28).
        var staticNames = new ResourceNames("app", id8); // slug irrelevant: StorageAccount = st{id8}
        try
        {
            var account = (await rg.GetStorageAccounts()
                .GetAsync(staticNames.StorageAccount, cancellationToken: cancellationToken)).Value;
            if (account.Data.PrimaryEndpoints?.WebUri is not null)
            {
                return SnapshotFromStaticWebsite(account, appId, ownerUserId: "");
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No storage account for this id8 — fall through to null.
        }

        return null;
    }

    public async Task<DeployedApp> EnsureProvisionedAsync(
        string appId,
        string ownerUserId,
        string appName,
        HostingCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        var names = ResourceNames.From(appId, appName);
        var plan = ProvisionPlan.From(capabilities);
        _logger.LogInformation(
            "provisioning user-app appId={AppId} slug8={Slug} id8={Id8} plan=[api={Api} cosmos={Cosmos} clerk={Clerk}]",
            appId, names.Slug8, names.Id8, plan.Api, plan.Cosmos, plan.ClerkOrg);

        // Static profile (no API, no Cosmos, no Clerk) → Azure Static Web App.
        // No App Service plan, so it sidesteps the F1 per-region plan cap and the
        // free-tier instance-capacity wall that block batch provisioning of F1
        // Web Apps. Full-stack apps keep the F1 Web App + Flex + Cosmos path below.
        if (!plan.Api && !plan.Cosmos && !plan.ClerkOrg)
        {
            return await ProvisionStaticWebsiteAsync(appId, ownerUserId, names, cancellationToken);
        }

        // ADR-0002 step #2 — Clerk Org first (cheap; fails fast on plan
        // misconfig). Only when the app has auth.
        string clerkPublishableKey = "";
        if (plan.ClerkOrg)
        {
            var clerkOrg = await _clerk.EnsureAsync(appId, appName, ownerUserId, cancellationToken);
            clerkPublishableKey = string.IsNullOrWhiteSpace(clerkOrg.PublishableKey)
                ? _opts.ClerkPublishableKeyFallback
                : clerkOrg.PublishableKey;
        }

        var rg = ResourceGroup();

        // Always: identity, telemetry, storage, the F1 plan + frontend Web App.
        var (miResourceId, miPrincipalId, miClientId) = await EnsureManagedIdentityAsync(rg, names, cancellationToken);
        var appInsightsConn = await EnsureAppInsightsAsync(rg, names, appId, cancellationToken);
        var storageAccountResourceId = await EnsureStorageAccountAsync(rg, names, cancellationToken);
        var f1PlanId = await EnsureF1PlanAsync(rg, names, cancellationToken);

        // An API app gets the shared bindings injected; a Static site is served
        // as plain files by `npx serve` and needs none.
        var sharedAppSettings = plan.Api
            ? BuildSharedAppSettings(names, clerkPublishableKey, appInsightsConn, miClientId)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var frontendHost = await EnsureFrontendWebAppAsync(
            rg, names, f1PlanId, miResourceId, sharedAppSettings, cancellationToken);

        if (plan.Api)
        {
            // Flex Consumption Function App deploys land in a blob container
            // inside the per-app storage account; the FunctionAppConfig points
            // at it but Azure does NOT auto-create it on the Function App side.
            // Create it explicitly so azure/functions-action@v1's OneDeploy
            // upload step has a real target on the first deploy.
            await EnsureFunctionAppDeploymentContainerAsync(storageAccountResourceId, names, cancellationToken);

            var flexPlanId = await EnsureFlexConsumptionPlanAsync(rg, names, cancellationToken);

            await EnsureFunctionAppAsync(
                rg, names, flexPlanId, miResourceId, storageAccountResourceId,
                sharedAppSettings, frontendHost, cancellationToken);

            // The Function App reads per-app secrets from the shared Key Vault.
            await EnsureKvSecretsUserRoleAssignmentAsync(miPrincipalId, cancellationToken);
        }

        if (plan.Cosmos)
        {
            await EnsureCosmosContainerAsync(names, cancellationToken);
            await EnsureCosmosRoleAssignmentAsync(miPrincipalId, names, cancellationToken);
        }

        return await FinishProvisioningAsync(
            appId, ownerUserId, names, frontendHost, clerkPublishableKey, plan, cancellationToken);
    }

    /// Shared tail of EnsureProvisionedAsync: the deploy SP + federated
    /// credential + scoped RBAC (always created — deploy.yml authenticates via
    /// OIDC for every profile), then the DeployedApp snapshot. Frontend-scope
    /// RBAC always; Function-App-scope only when the plan has an API. The
    /// DeployedApp's API/Cosmos/KeyVault/Clerk fields are null for slots the
    /// plan didn't provision.
    private async Task<DeployedApp> FinishProvisioningAsync(
        string appId,
        string ownerUserId,
        ResourceNames names,
        string? frontendHost,
        string clerkPublishableKey,
        ProvisionPlan plan,
        CancellationToken cancellationToken)
    {
        // ADR-0002 step #4 — deploy SP + federated credential. The repo must
        // already exist (step #1: SourceControl module's EnsureUserAppRepoAsync)
        // for the federated credential subject to be valid on first push.
        var repoName = names.RepoName(_opts.UserAppRepoNamePrefix);
        // Legacy in-process path: repo ids unavailable here — classic-subject-only (warns in the
        // provisioner). All new-path provisions carry ids via ProvisionSpec.Repo (F5).
        var deployIdentity = await _deployIdentity.EnsureAsync(
            appId,
            _opts.UserAppRepoOwner,
            repoName,
            _opts.UserAppDefaultBranch,
            cancellationToken: cancellationToken);

        // Give the deploy SP just enough RBAC to push code to its OWN Web
        // App (+ Function App for full-stack) and nothing else. Website
        // Contributor is narrower than Contributor — covers
        // Microsoft.Web/sites/publish + /config/list which is what
        // azure/webapps-deploy@v3 and azure/functions-action@v1 call.
        if (Guid.TryParse(deployIdentity.ServicePrincipalObjectId, out var deploySpPrincipalId))
        {
            var frontendWebAppScope = new ResourceIdentifier(
                $"/subscriptions/{_opts.SubscriptionId}/resourceGroups/{_opts.ResourceGroup}" +
                $"/providers/Microsoft.Web/sites/{names.FrontendWebApp}");
            await EnsureWebsiteContributorAsync(deploySpPrincipalId, frontendWebAppScope, cancellationToken);

            // No Function App without the API tier — skip its deploy scope.
            if (plan.Api)
            {
                var functionAppScope = new ResourceIdentifier(
                    $"/subscriptions/{_opts.SubscriptionId}/resourceGroups/{_opts.ResourceGroup}" +
                    $"/providers/Microsoft.Web/sites/{names.FunctionApp}");
                await EnsureWebsiteContributorAsync(deploySpPrincipalId, functionAppScope, cancellationToken);
            }

            // `azure/login@v2` calls `az account list` after authentication,
            // which requires the SP to have at least Reader at subscription
            // *or* RG scope. Without it: "No subscriptions found for ***" →
            // deploy fails before reaching the Web App / Function App.
            // RG-scope Reader is the smallest grant that satisfies the
            // enumeration without leaking sibling user-apps.
            var rgScope = new ResourceIdentifier(
                $"/subscriptions/{_opts.SubscriptionId}/resourceGroups/{_opts.ResourceGroup}");
            await EnsureReaderAsync(deploySpPrincipalId, rgScope, cancellationToken);
        }
        else
        {
            // Stub provisioner returns a non-GUID; skip role assignments in
            // that case. Production stack only ever sees real GUIDs from
            // EntraDeployIdentityProvisioner.
            _logger.LogWarning(
                "deploy SP role assignments skipped — non-GUID principal id {Id} (stub provisioner?)",
                deployIdentity.ServicePrincipalObjectId);
        }

        var hostedUrl = string.IsNullOrEmpty(frontendHost) ? null : $"https://{frontendHost}";
        var now = DateTimeOffset.UtcNow;

        return new DeployedApp(
            AppId: appId,
            OwnerUserId: ownerUserId,
            Status: DeployedAppStatus.Live,
            Subdomain: hostedUrl,
            ResourceGroupName: _opts.ResourceGroup,
            AppServicePlanName: names.AppServicePlan,
            FrontendWebAppName: names.FrontendWebApp,
            // Each tier's fields are null when the plan didn't provision it, so
            // the release gate skips the checks bound to absent slots (e.g.
            // api-health when there's no API, Cosmos checks when there's no DB).
            FlexConsumptionPlanName: plan.Api ? names.FlexConsumptionPlan : null,
            ApiFunctionAppName: plan.Api ? names.FunctionApp : null,
            StorageAccountName: names.StorageAccount,
            ManagedIdentityName: names.ManagedIdentity,
            AppInsightsName: names.AppInsights,
            CosmosAccountName: plan.Cosmos ? _opts.UserdataCosmosAccountName : null,
            CosmosContainerName: plan.Cosmos ? names.CosmosContainer : null,
            KeyVaultName: plan.Api ? _opts.KeyVaultName : null,
            KeyVaultSecretPrefix: plan.Api ? names.KvSecretPrefix : null,
            ClerkPublishableKey: plan.ClerkOrg ? clerkPublishableKey : null,
            CreatedAt: now,
            UpdatedAt: now);
    }

    /// Static-profile provisioning: an Azure **Storage static website** (a StorageV2
    /// account with the `$web` container enabled — no App Service plan, no SWA, so
    /// it sidesteps the Free-SWA 10-per-subscription cap) + the deploy SP/federated
    /// credential, with the SP granted Storage Blob Data Contributor on the account
    /// (`az storage blob upload-batch` publish) and Reader on the RG (azure/login).
    /// The storage account name rides in DeployedApp.FrontendWebAppName so the
    /// deploy workflow targets it with no extra plumbing.
    private async Task<DeployedApp> ProvisionStaticWebsiteAsync(
        string appId, string ownerUserId, ResourceNames names, CancellationToken cancellationToken)
    {
        var rg = ResourceGroup();
        var webEndpoint = await EnsureStaticWebsiteEnabledAsync(rg, names, cancellationToken);

        var repoName = names.RepoName(_opts.UserAppRepoNamePrefix);
        var deployIdentity = await _deployIdentity.EnsureAsync(
            appId, _opts.UserAppRepoOwner, repoName, _opts.UserAppDefaultBranch, cancellationToken: cancellationToken);

        if (Guid.TryParse(deployIdentity.ServicePrincipalObjectId, out var deploySpPrincipalId))
        {
            var accountScope = new ResourceIdentifier(
                $"/subscriptions/{_opts.SubscriptionId}/resourceGroups/{_opts.ResourceGroup}" +
                $"/providers/Microsoft.Storage/storageAccounts/{names.StorageAccount}");
            await EnsureRoleAssignmentAsync(
                deploySpPrincipalId, accountScope, StorageBlobDataContributorRoleDefinitionId, "storage-blob-data-contributor", cancellationToken);

            var rgScope = new ResourceIdentifier(
                $"/subscriptions/{_opts.SubscriptionId}/resourceGroups/{_opts.ResourceGroup}");
            await EnsureReaderAsync(deploySpPrincipalId, rgScope, cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "static deploy SP role assignments skipped — non-GUID principal id {Id} (stub provisioner?)",
                deployIdentity.ServicePrincipalObjectId);
        }

        var now = DateTimeOffset.UtcNow;
        return new DeployedApp(
            AppId: appId,
            OwnerUserId: ownerUserId,
            Status: DeployedAppStatus.Live,
            Subdomain: webEndpoint,
            ResourceGroupName: _opts.ResourceGroup,
            AppServicePlanName: null,                  // no App Service plan
            FrontendWebAppName: names.StorageAccount,   // storage account → deploy.yml target
            FlexConsumptionPlanName: null,
            ApiFunctionAppName: null,
            StorageAccountName: names.StorageAccount,
            ManagedIdentityName: null,
            AppInsightsName: null,
            CosmosAccountName: null,
            CosmosContainerName: null,
            KeyVaultName: null,
            KeyVaultSecretPrefix: null,
            ClerkPublishableKey: null,
            CreatedAt: now,
            UpdatedAt: now);
    }

    /// Creates (or reuses) the `st{id8}` storage account and turns on the **static
    /// website** feature (`$web`, index + 404 → index.html). Static website is a
    /// data-plane setting (not exposed by the ARM SDK), so it's set via the Blob
    /// SDK using an account key (the provisioning MI can list keys with Contributor;
    /// the key avoids RBAC-propagation delay on first use). Returns the public web
    /// endpoint (`https://st{id8}.z##.web.core.windows.net`, trailing slash stripped).
    private async Task<string?> EnsureStaticWebsiteEnabledAsync(
        ResourceGroupResource rg, ResourceNames names, CancellationToken ct)
    {
        await EnsureStorageAccountAsync(rg, names, ct);
        var account = (await rg.GetStorageAccounts().GetAsync(names.StorageAccount, cancellationToken: ct)).Value;

        var keys = new List<StorageAccountKey>();
        await foreach (var k in account.GetKeysAsync(cancellationToken: ct)) keys.Add(k);
        var key = keys.FirstOrDefault()?.Value
            ?? throw new InvalidOperationException($"storage account {names.StorageAccount} returned no keys");

        var blobUri = account.Data.PrimaryEndpoints?.BlobUri
            ?? new Uri($"https://{names.StorageAccount}.blob.core.windows.net");
        var blob = new BlobServiceClient(blobUri, new StorageSharedKeyCredential(names.StorageAccount, key));
        var props = (await blob.GetPropertiesAsync(ct)).Value;
        props.StaticWebsite = new BlobStaticWebsite
        {
            Enabled = true,
            IndexDocument = "index.html",
            ErrorDocument404Path = "index.html", // soft fallback for deep-link/404s
        };
        await blob.SetPropertiesAsync(props, ct);
        _logger.LogInformation("static website enabled on storage account name={Name}", names.StorageAccount);

        // Re-fetch: the web endpoint is only populated once static website is on.
        account = (await rg.GetStorageAccounts().GetAsync(names.StorageAccount, cancellationToken: ct)).Value;
        var web = account.Data.PrimaryEndpoints?.WebUri?.ToString();
        return string.IsNullOrEmpty(web) ? null : web.TrimEnd('/');
    }

    /// Idempotent role assignment for a service principal on a scope. Deterministic
    /// assignment id so re-runs converge; 409/RoleAssignmentExists is success.
    private async Task EnsureRoleAssignmentAsync(
        Guid principalId, ResourceIdentifier scope, string roleDefinitionId, string label, CancellationToken ct)
    {
        var roleDefId = new ResourceIdentifier(
            $"/subscriptions/{_opts.SubscriptionId}/providers/Microsoft.Authorization/" +
            $"roleDefinitions/{roleDefinitionId}");

        var assignmentId = DeterministicGuid($"{principalId}|{scope}|{label}");
        var assignments  = _arm.GetRoleAssignments(scope);

        var content = new RoleAssignmentCreateOrUpdateContent(roleDefId, principalId)
        {
            PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
        };

        try
        {
            await assignments.CreateOrUpdateAsync(WaitUntil.Completed, assignmentId.ToString(), content, ct);
            _logger.LogInformation("{Label} assigned principal={Principal} scope={Scope}", label, principalId, scope);
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.ErrorCode == "RoleAssignmentExists")
        {
            _logger.LogDebug("{Label} already assigned principal={Principal} scope={Scope}", label, principalId, scope);
        }
    }

    public Task QuarantineAsync(string appId, string reason, CancellationToken cancellationToken = default)
    {
        // v1.1: stop the frontend Web App + disable the Function App. Today:
        // log only — quarantine policy is still pending (#10) and the
        // operational behaviour (502s until quota resets) is acceptable in v1.
        _logger.LogWarning("quarantine requested but not yet implemented appId={AppId} reason={Reason}",
            appId, reason);
        return Task.CompletedTask;
    }

    public Task<int> CleanupE2eTestUsersAsync(string appId, CancellationToken cancellationToken = default) =>
        _clerk.CleanupE2eTestUsersAsync(appId, cancellationToken);

    // ------------------------------------------------------------------
    // Idempotent per-resource helpers
    // ------------------------------------------------------------------

    private ResourceGroupResource ResourceGroup() =>
        _arm.GetDefaultSubscription().GetResourceGroup(_opts.ResourceGroup).Value;

    public async Task DeprovisionAsync(string appId, string appName, CancellationToken cancellationToken = default)
    {
        var names = ResourceNames.From(appId, appName);
        _logger.LogInformation("deprovisioning app {AppId} id8={Id8}", appId, names.Id8);

        var rg = ResourceGroup();

        // Web apps before their plans; storage / identity / app-insights last.
        await DeleteWebSiteAsync(rg, names.FrontendWebApp, cancellationToken);
        await DeleteWebSiteAsync(rg, names.FunctionApp, cancellationToken);
        // Static-profile apps have a Static Web App instead of the frontend Web
        // App (best-effort — only one of the two exists for a given app).
        await DeleteResourceAsync("static web app", names.StaticWebApp,
            async () => (await rg.GetStaticSites().GetAsync(names.StaticWebApp, cancellationToken)).Value,
            r => r.DeleteAsync(WaitUntil.Completed, cancellationToken));
        await DeleteResourceAsync("app service plan", names.AppServicePlan,
            async () => (await rg.GetAppServicePlans().GetAsync(names.AppServicePlan, cancellationToken)).Value,
            r => r.DeleteAsync(WaitUntil.Completed, cancellationToken));
        await DeleteResourceAsync("flex plan", names.FlexConsumptionPlan,
            async () => (await rg.GetAppServicePlans().GetAsync(names.FlexConsumptionPlan, cancellationToken)).Value,
            r => r.DeleteAsync(WaitUntil.Completed, cancellationToken));
        await DeleteResourceAsync("storage account", names.StorageAccount,
            async () => (await rg.GetStorageAccounts().GetAsync(names.StorageAccount, cancellationToken: cancellationToken)).Value,
            r => r.DeleteAsync(WaitUntil.Completed, cancellationToken));
        await DeleteResourceAsync("managed identity", names.ManagedIdentity,
            async () => (await rg.GetUserAssignedIdentities().GetAsync(names.ManagedIdentity, cancellationToken)).Value,
            r => r.DeleteAsync(WaitUntil.Completed, cancellationToken));
        await DeleteResourceAsync("app insights", names.AppInsights,
            async () => (await rg.GetApplicationInsightsComponents().GetAsync(names.AppInsights, cancellationToken)).Value,
            r => r.DeleteAsync(WaitUntil.Completed, cancellationToken));
        await DeleteSmartDetectorAlertRuleAsync(rg, names.FailureAnomaliesAlertRule, cancellationToken);

        await DeleteCosmosContainerAsync(names, cancellationToken);

        // Clerk Org + deploy SP / app registration (Microsoft Graph) via the
        // providers that created them. RemoveAsync is a no-op when absent.
        await SafeAsync("clerk org remove", appId, () => _clerk.RemoveAsync(appId, cancellationToken));
        await SafeAsync("deploy identity remove", appId, () => _deployIdentity.RemoveAsync(appId, cancellationToken));

        _logger.LogInformation("deprovision complete app {AppId}", appId);
    }

    public async Task DeprovisionByAppIdAsync(string appId, CancellationToken cancellationToken = default)
    {
        // The /deprovision contract carries only appId (no appName → no slug8), so we can't re-derive the
        // exact resource names. Every per-app name embeds the appId-derived id8 (see ResourceNames), so
        // enumerate the shared RG and delete by id8 match, in dependency order: sites (frontend Web App +
        // Function App are both Microsoft.Web/sites) and static sites first, then plans (a plan won't
        // delete while a site references it), then storage / identity / app-insights. Same best-effort,
        // idempotent semantics as DeprovisionAsync — a failure on one resource is logged and the rest run.
        var id8 = ResourceNames.From(appId, appName: string.Empty).Id8;
        _logger.LogInformation("deprovisioning app {AppId} by id8={Id8}", appId, id8);
        bool Match(string name) => name.Contains(id8, StringComparison.OrdinalIgnoreCase);

        var rg = ResourceGroup();

        await foreach (var site in rg.GetWebSites().GetAllAsync(cancellationToken: cancellationToken))
            if (Match(site.Data.Name))
                await SafeAsync($"delete web app {site.Data.Name}", appId,
                    () => site.DeleteAsync(WaitUntil.Completed, deleteMetrics: true, deleteEmptyServerFarm: false, cancellationToken: cancellationToken));

        await foreach (var swa in rg.GetStaticSites().GetAllAsync(cancellationToken: cancellationToken))
            if (Match(swa.Data.Name))
                await SafeAsync($"delete static web app {swa.Data.Name}", appId,
                    () => swa.DeleteAsync(WaitUntil.Completed, cancellationToken));

        await foreach (var plan in rg.GetAppServicePlans().GetAllAsync(cancellationToken: cancellationToken))
            if (Match(plan.Data.Name))
                await SafeAsync($"delete plan {plan.Data.Name}", appId,
                    () => plan.DeleteAsync(WaitUntil.Completed, cancellationToken));

        await foreach (var storage in rg.GetStorageAccounts().GetAllAsync(cancellationToken: cancellationToken))
            if (Match(storage.Data.Name))
                await SafeAsync($"delete storage {storage.Data.Name}", appId,
                    () => storage.DeleteAsync(WaitUntil.Completed, cancellationToken));

        await foreach (var mi in rg.GetUserAssignedIdentities().GetAllAsync(cancellationToken: cancellationToken))
            if (Match(mi.Data.Name))
                await SafeAsync($"delete identity {mi.Data.Name}", appId,
                    () => mi.DeleteAsync(WaitUntil.Completed, cancellationToken));

        await foreach (var ai in rg.GetApplicationInsightsComponents().GetAllAsync(cancellationToken: cancellationToken))
            if (Match(ai.Data.Name))
                await SafeAsync($"delete app insights {ai.Data.Name}", appId,
                    () => ai.DeleteAsync(WaitUntil.Completed, cancellationToken));

        // The component's companion "Failure Anomalies" alert rule embeds the
        // component name (and therefore the id8), so enumerate rules rather
        // than re-deriving the name — this also sweeps rules whose component
        // was already deleted by an earlier partial run.
        await SafeAsync("delete failure-anomalies alert rules", appId, async () =>
        {
            await foreach (var rule in rg.GetGenericResourcesAsync(
                filter: $"resourceType eq '{SmartDetectorAlertRuleType}'",
                cancellationToken: cancellationToken))
                if (Match(rule.Data.Name))
                    await SafeAsync($"delete alert rule {rule.Data.Name}", appId,
                        () => rule.DeleteAsync(WaitUntil.Completed, cancellationToken));
        });

        // Cosmos container lives in the shared serverless account (separate RG) — enumerate + id8 match.
        await SafeAsync("delete cosmos container", appId, async () =>
        {
            var account = _arm.GetCosmosDBAccountResource(CosmosAccountResourceId());
            var db = (await account.GetCosmosDBSqlDatabaseAsync(_opts.UserdataCosmosDatabase, cancellationToken)).Value;
            await foreach (var c in db.GetCosmosDBSqlContainers().GetAllAsync(cancellationToken: cancellationToken))
                if (Match(c.Data.Name))
                    await c.DeleteAsync(WaitUntil.Completed, cancellationToken);
        });

        // Clerk Org + deploy SP / app registration — already appId-keyed.
        await SafeAsync("clerk org remove", appId, () => _clerk.RemoveAsync(appId, cancellationToken));
        await SafeAsync("deploy identity remove", appId, () => _deployIdentity.RemoveAsync(appId, cancellationToken));

        _logger.LogInformation("deprovision-by-appId complete app {AppId}", appId);
    }

    public async Task<(long MinorUnits, string Currency)> GetHostingSpendByAppIdAsync(
        string appId, CancellationToken cancellationToken = default)
    {
        // Month-to-date actual cost for one app, via the Cost Management REST query (the ARM SDK surface
        // for this is awkward; the REST contract is stable). All per-app resources share one RG, so group
        // by ResourceId and sum the rows whose resource id carries this app's id8 — same id8 convention as
        // DeprovisionByAppIdAsync, no per-resource tag needed. Requires Cost Management Reader on the
        // subscription; until that's granted (or before any cost accrues) the query fails or returns no
        // matching rows and we return 0, so the platform's daily /spend relay stays inert rather than erroring.
        const string fallbackCurrency = "GBP";
        try
        {
            if (string.IsNullOrWhiteSpace(_opts.SubscriptionId))
                return (0, fallbackCurrency);

            var id8 = ResourceNames.From(appId, appName: string.Empty).Id8;
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }), cancellationToken);

            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var url = $"https://management.azure.com/subscriptions/{_opts.SubscriptionId}" +
                      "/providers/Microsoft.CostManagement/query?api-version=2023-11-01";
            var queryBody = new
            {
                type = "ActualCost",
                timeframe = "MonthToDate",
                dataset = new
                {
                    granularity = "None",
                    aggregation = new { totalCost = new { name = "Cost", function = "Sum" } },
                    grouping = new[] { new { type = "Dimension", name = "ResourceId" } },
                },
            };

            using var response = await http.PostAsJsonAsync(url, queryBody, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("spend query for {AppId} returned {Status} — treating as 0", appId, (int)response.StatusCode);
                return (0, fallbackCurrency);
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var props = doc.GetProperty("properties");

            // Resolve column indices from the response rather than assuming order.
            int costIdx = -1, resourceIdx = -1, currencyIdx = -1, i = 0;
            foreach (var col in props.GetProperty("columns").EnumerateArray())
            {
                switch (col.GetProperty("name").GetString())
                {
                    case "Cost" or "PreTaxCost" or "CostUSD": costIdx = i; break;
                    case "ResourceId": resourceIdx = i; break;
                    case "Currency": currencyIdx = i; break;
                }
                i++;
            }
            if (costIdx < 0 || resourceIdx < 0)
                return (0, fallbackCurrency);

            decimal total = 0;
            var currency = fallbackCurrency;
            foreach (var row in props.GetProperty("rows").EnumerateArray())
            {
                var resourceId = row[resourceIdx].GetString() ?? string.Empty;
                if (!resourceId.Contains(id8, StringComparison.OrdinalIgnoreCase))
                    continue;
                total += row[costIdx].GetDecimal();
                if (currencyIdx >= 0)
                    currency = row[currencyIdx].GetString() ?? fallbackCurrency;
            }

            return ((long)Math.Round(total * 100m, MidpointRounding.AwayFromZero), currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "spend query failed for {AppId} — returning 0", appId);
            return (0, fallbackCurrency);
        }
    }

    private async Task DeleteWebSiteAsync(ResourceGroupResource rg, string name, CancellationToken ct) =>
        await DeleteResourceAsync("web app", name,
            async () => (await rg.GetWebSites().GetAsync(name, ct)).Value,
            // deleteEmptyServerFarm:false — plans are deleted explicitly above.
            r => r.DeleteAsync(WaitUntil.Completed, deleteMetrics: true, deleteEmptyServerFarm: false, cancellationToken: ct));

    // No typed SDK package is referenced for microsoft.alertsmanagement, so the
    // auto-created "Failure Anomalies" smart-detector alert rule is deleted
    // through the untyped ARM surface (api-version resolved from provider metadata).
    private const string SmartDetectorAlertRuleType =
        "microsoft.alertsmanagement/smartDetectorAlertRules";

    private async Task DeleteSmartDetectorAlertRuleAsync(
        ResourceGroupResource rg, string ruleName, CancellationToken ct) =>
        await DeleteResourceAsync("smart-detector alert rule", ruleName,
            () => Task.FromResult(_arm.GetGenericResource(new ResourceIdentifier(
                $"{rg.Id}/providers/{SmartDetectorAlertRuleType}/{ruleName}"))),
            r => r.DeleteAsync(WaitUntil.Completed, ct));

    private async Task DeleteCosmosContainerAsync(ResourceNames names, CancellationToken ct) =>
        await DeleteResourceAsync("cosmos container", names.CosmosContainer,
            async () =>
            {
                var account = _arm.GetCosmosDBAccountResource(CosmosAccountResourceId());
                var db = (await account.GetCosmosDBSqlDatabaseAsync(_opts.UserdataCosmosDatabase, ct)).Value;
                return (await db.GetCosmosDBSqlContainers().GetAsync(names.CosmosContainer, ct)).Value;
            },
            r => r.DeleteAsync(WaitUntil.Completed, ct));

    /// Get-then-delete a single resource, best-effort: a 404 (already gone)
    /// is success; any other failure is logged and swallowed so the rest of
    /// the teardown still runs.
    private async Task DeleteResourceAsync<T>(
        string kind, string name, Func<Task<T>> get, Func<T, Task> delete)
    {
        try
        {
            var resource = await get();
            await delete(resource);
            _logger.LogInformation("deprovision: deleted {Kind} {Name}", kind, name);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("deprovision: {Kind} {Name} already gone", kind, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "deprovision: {Kind} {Name} delete failed — left for the manual sweep", kind, name);
        }
    }

    private async Task SafeAsync(string what, string appId, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _logger.LogWarning(ex, "deprovision: {What} failed appId={AppId} — left for the manual sweep", what, appId); }
    }

    private async Task<(ResourceIdentifier Id, Guid PrincipalId, string ClientId)> EnsureManagedIdentityAsync(
        ResourceGroupResource rg, ResourceNames names, CancellationToken ct)
    {
        var coll = rg.GetUserAssignedIdentities();
        UserAssignedIdentityResource mi;
        try
        {
            mi = (await coll.GetAsync(names.ManagedIdentity, ct)).Value;
            _logger.LogDebug("UAMI exists name={Name}", names.ManagedIdentity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var data = new UserAssignedIdentityData(_opts.Location);
            data.Tags.Add("product", "yorrixx");
            mi = (await coll.CreateOrUpdateAsync(WaitUntil.Completed, names.ManagedIdentity, data, ct)).Value;
            _logger.LogInformation("UAMI created name={Name}", names.ManagedIdentity);
        }

        var principalId = mi.Data.PrincipalId
            ?? throw new InvalidOperationException($"UAMI {names.ManagedIdentity} has no principalId.");
        var clientId = mi.Data.ClientId?.ToString()
            ?? throw new InvalidOperationException($"UAMI {names.ManagedIdentity} has no clientId.");
        return (mi.Id, principalId, clientId);
    }

    private async Task<string> EnsureAppInsightsAsync(
        ResourceGroupResource rg, ResourceNames names, string appId, CancellationToken ct)
    {
        var coll = rg.GetApplicationInsightsComponents();
        ApplicationInsightsComponentResource component;
        try
        {
            component = (await coll.GetAsync(names.AppInsights, ct)).Value;
            _logger.LogDebug("AI component exists name={Name}", names.AppInsights);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var data = new ApplicationInsightsComponentData(_opts.Location, kind: "web")
            {
                ApplicationType = ApplicationInsightsApplicationType.Web,
                IngestionMode = ComponentIngestionMode.LogAnalytics,
                WorkspaceResourceId = new ResourceIdentifier(_opts.AppInsightsWorkspaceId),
            };
            data.Tags.Add("product", "yorrixx");
            data.Tags.Add("appId", appId);
            component = (await coll.CreateOrUpdateAsync(WaitUntil.Completed, names.AppInsights, data, ct)).Value;
            _logger.LogInformation("AI component created name={Name}", names.AppInsights);
        }

        return component.Data.ConnectionString
            ?? throw new InvalidOperationException($"AI component {names.AppInsights} has no connection string.");
    }

    private async Task<ResourceIdentifier> EnsureStorageAccountAsync(
        ResourceGroupResource rg, ResourceNames names, CancellationToken ct)
    {
        var coll = rg.GetStorageAccounts();
        StorageAccountResource account;
        try
        {
            account = (await coll.GetAsync(names.StorageAccount, cancellationToken: ct)).Value;
            _logger.LogDebug("storage account exists name={Name}", names.StorageAccount);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var sku = new StorageSku(StorageSkuName.StandardLrs);
            var content = new StorageAccountCreateOrUpdateContent(sku, StorageKind.StorageV2, _opts.Location)
            {
                AllowBlobPublicAccess = false,
                MinimumTlsVersion = StorageMinimumTlsVersion.Tls1_2,
            };
            content.Tags.Add("product", "yorrixx");
            try
            {
                account = (await coll.CreateOrUpdateAsync(WaitUntil.Completed, names.StorageAccount, content, ct)).Value;
                _logger.LogInformation("storage account created name={Name}", names.StorageAccount);
            }
            catch (RequestFailedException inProgress) when (ProvisioningRetry.IsOperationInProgress(inProgress))
            {
                // D15 (HealthyChicken): ARM accepted a create for this exact account (ours — the name is
                // deterministic) that is still running: the SDK's own transient re-PUT, a redelivered
                // message, or a prior attempt. Wait for it and ADOPT the account; the observed 409
                // carried Retry-After: 40, and the real operation completed within ~15 minutes.
                _logger.LogWarning(
                    "storage create hit an in-progress operation name={Name} — polling for its completion",
                    names.StorageAccount);
                var adopted = await ProvisioningRetry.PollForResourceAsync(
                    async token =>
                    {
                        try { return (await coll.GetAsync(names.StorageAccount, cancellationToken: token)).Value; }
                        catch (RequestFailedException g) when (g.Status == 404) { return null; }
                    },
                    attempts: 24, delay: TimeSpan.FromSeconds(40), ct);  // ≤ ~16 min, matches Retry-After
                if (adopted is null)
                {
                    // Never materialized → surface the original conflict honestly (stack preserved).
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inProgress).Throw();
                }
                account = adopted!;
                _logger.LogInformation("storage account adopted after in-progress operation name={Name}", names.StorageAccount);
            }
        }
        return account.Id;
    }

    private async Task<ResourceIdentifier> EnsureF1PlanAsync(
        ResourceGroupResource rg, ResourceNames names, CancellationToken ct)
    {
        var coll = rg.GetAppServicePlans();
        AppServicePlanResource plan;
        try
        {
            plan = (await coll.GetAsync(names.AppServicePlan, ct)).Value;
            _logger.LogDebug("F1 plan exists name={Name}", names.AppServicePlan);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var data = new AppServicePlanData(_opts.Location)
            {
                Sku = new AppServiceSkuDescription
                {
                    Name = "F1",
                    Tier = "Free",
                    Size = "F1",
                    Family = "F",
                    Capacity = 0,
                },
                Kind = "linux",
                IsReserved = true,
            };
            data.Tags.Add("product", "yorrixx");
            plan = (await coll.CreateOrUpdateAsync(WaitUntil.Completed, names.AppServicePlan, data, ct)).Value;
            _logger.LogInformation("F1 plan created name={Name}", names.AppServicePlan);
        }
        return plan.Id;
    }

    private async Task<ResourceIdentifier> EnsureFlexConsumptionPlanAsync(
        ResourceGroupResource rg, ResourceNames names, CancellationToken ct)
    {
        var coll = rg.GetAppServicePlans();
        AppServicePlanResource plan;
        try
        {
            plan = (await coll.GetAsync(names.FlexConsumptionPlan, ct)).Value;
            _logger.LogDebug("flex plan exists name={Name}", names.FlexConsumptionPlan);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var data = new AppServicePlanData(_opts.Location)
            {
                Sku = new AppServiceSkuDescription
                {
                    Name = "FC1",
                    Tier = "FlexConsumption",
                },
                Kind = "functionapp",
                IsReserved = true,
            };
            data.Tags.Add("product", "yorrixx");
            plan = (await coll.CreateOrUpdateAsync(WaitUntil.Completed, names.FlexConsumptionPlan, data, ct)).Value;
            _logger.LogInformation("flex plan created name={Name}", names.FlexConsumptionPlan);
        }
        return plan.Id;
    }

    private async Task<string?> EnsureFrontendWebAppAsync(
        ResourceGroupResource rg,
        ResourceNames names,
        ResourceIdentifier planId,
        ResourceIdentifier miResourceId,
        IDictionary<string, string> sharedAppSettings,
        CancellationToken ct)
    {
        var coll = rg.GetWebSites();
        WebSiteResource site;
        try
        {
            site = (await coll.GetAsync(names.FrontendWebApp, ct)).Value;
            _logger.LogDebug("frontend Web App exists name={Name}", names.FrontendWebApp);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var identity = new ManagedServiceIdentity(ManagedServiceIdentityType.UserAssigned);
            identity.UserAssignedIdentities[miResourceId] = new Azure.ResourceManager.Models.UserAssignedIdentity();

            var data = new WebSiteData(_opts.Location)
            {
                AppServicePlanId = planId,
                Identity = identity,
                IsReserved = true,
                Kind = "app,linux",
                SiteConfig = new SiteConfigProperties
                {
                    LinuxFxVersion = "NODE|22-lts",
                    IsHttp20Enabled = false, // F1 does not support HTTP/2
                    // Vite emits a static SPA but Linux Node Web Apps don't
                    // serve static files natively. `serve -s .` runs a tiny
                    // static server with SPA fallback (everything →
                    // /index.html). `-s .` is "current directory" — Linux
                    // Web Apps run the startup command from
                    // `/home/site/wwwroot`, which is exactly where
                    // azure/webapps-deploy@v3 lands the zip contents. `-s
                    // wwwroot` would resolve to `/home/site/wwwroot/wwwroot`
                    // and 404. `npx` fetches `serve` on first start.
                    AppCommandLine = "npx serve -s . -l 8080",
                },
            };
            data.Tags.Add("product", "yorrixx");
            site = (await coll.CreateOrUpdateAsync(WaitUntil.Completed, names.FrontendWebApp, data, ct)).Value;
            _logger.LogInformation("frontend Web App created name={Name}", names.FrontendWebApp);
        }

        await UpdateAppSettingsAsync(site, sharedAppSettings, ct);
        return site.Data.DefaultHostName;
    }

    private async Task EnsureFunctionAppAsync(
        ResourceGroupResource rg,
        ResourceNames names,
        ResourceIdentifier flexPlanId,
        ResourceIdentifier miResourceId,
        ResourceIdentifier storageAccountResourceId,
        IDictionary<string, string> sharedAppSettings,
        string? frontendHost,
        CancellationToken ct)
    {
        var coll = rg.GetWebSites();
        WebSiteResource site;
        try
        {
            site = (await coll.GetAsync(names.FunctionApp, ct)).Value;
            _logger.LogDebug("function app exists name={Name}", names.FunctionApp);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var identity = new ManagedServiceIdentity(ManagedServiceIdentityType.UserAssigned);
            identity.UserAssignedIdentities[miResourceId] = new Azure.ResourceManager.Models.UserAssignedIdentity();

            // Flex Consumption uses a blob container in the per-app storage
            // account as the deployment-package location. Authentication is
            // via AzureWebJobsStorage connection string (set in app settings)
            // so the runtime can read/write the package.
            var deploymentBlobUri = new Uri(
                $"https://{names.StorageAccount}.blob.core.windows.net/app-package-{names.FunctionApp}");

            var data = new WebSiteData(_opts.Location)
            {
                AppServicePlanId = flexPlanId,
                Identity = identity,
                Kind = "functionapp,linux",
                IsReserved = true,
                FunctionAppConfig = new FunctionAppConfig
                {
                    // SDK 1.3.0 surfaces the deployment-package storage as a
                    // top-level DeploymentStorage property; the nested
                    // FunctionsDeployment wrapper exists in the schema but is
                    // not part of the public API surface.
                    DeploymentStorage = new FunctionAppStorage
                    {
                        StorageType = FunctionAppStorageType.BlobContainer,
                        Value = deploymentBlobUri,
                        Authentication = new FunctionAppStorageAuthentication
                        {
                            AuthenticationType = FunctionAppStorageAccountAuthenticationType.StorageAccountConnectionString,
                            StorageAccountConnectionStringName = "AzureWebJobsStorage",
                        },
                    },
                    Runtime = new FunctionAppRuntime
                    {
                        Name = FunctionAppRuntimeName.DotnetIsolated,
                        Version = "9.0",
                    },
                    ScaleAndConcurrency = new FunctionAppScaleAndConcurrency
                    {
                        FunctionAppInstanceMemoryMB = 2048,
                        FunctionAppMaximumInstanceCount = 100,
                        // 16 concurrent HTTP invocations per instance — Flex
                        // Consumption default. Bump per-app if cold start
                        // becomes a complaint (see ADR-0003 "always-ready").
                        HttpPerInstanceConcurrency = 16,
                    },
                },
            };
            data.Tags.Add("product", "yorrixx");
            site = (await coll.CreateOrUpdateAsync(WaitUntil.Completed, names.FunctionApp, data, ct)).Value;
            _logger.LogInformation("function app created name={Name}", names.FunctionApp);
        }

        // Function App needs AzureWebJobsStorage on top of the shared settings.
        var functionSettings = new Dictionary<string, string>(sharedAppSettings, StringComparer.Ordinal)
        {
            ["AzureWebJobsStorage"] = await GetStorageConnectionStringAsync(storageAccountResourceId, ct),
        };
        await UpdateAppSettingsAsync(site, functionSettings, ct);

        // CORS: the SPA on the F1 Web App calls this Function App cross-origin
        // (via VITE_API_URL). Applied on every provision pass so re-provision
        // heals apps created before this existed. localhost is for `vite dev`.
        if (!string.IsNullOrWhiteSpace(frontendHost))
        {
            var cors = new AppServiceCorsSettings();
            cors.AllowedOrigins.Add($"https://{frontendHost}");
            cors.AllowedOrigins.Add("http://localhost:5173");
            await site.GetWebSiteConfig().UpdateAsync(new SiteConfigData { Cors = cors }, ct);
            _logger.LogInformation("function app CORS set name={Name} origin=https://{Origin}",
                names.FunctionApp, frontendHost);
        }
    }

    private async Task EnsureFunctionAppDeploymentContainerAsync(
        ResourceIdentifier storageAccountId, ResourceNames names, CancellationToken ct)
    {
        var containerName = $"app-package-{names.FunctionApp}";
        var account = _arm.GetStorageAccountResource(storageAccountId);

        // ARM returns from the storage CreateOrUpdate when the account
        // resource exists, but `ProvisioningState` can briefly remain
        // `ResolvingDNS` (or similar) before flipping to `Succeeded` —
        // and the blob service / container subresource APIs 409 with
        // `StorageAccountIsNotProvisioned` until that flip happens.
        // Poll the parent for up to 60s.
        const int maxAttempts = 12;
        var delay = TimeSpan.FromSeconds(5);
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var refreshed = await account.GetAsync(cancellationToken: ct);
            var state = refreshed.Value.Data.ProvisioningState?.ToString();
            if (state == "Succeeded") break;

            if (attempt == maxAttempts - 1)
            {
                throw new InvalidOperationException(
                    $"Storage account {storageAccountId.Name} did not reach Succeeded after " +
                    $"{maxAttempts * delay.TotalSeconds}s (last state: {state ?? "unknown"}).");
            }
            _logger.LogDebug(
                "storage account {Name} provisioning state {State}; waiting {Delay}s for Succeeded",
                storageAccountId.Name, state, delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }

        var blobService = account.GetBlobService();
        var containers = blobService.GetBlobContainers();

        try
        {
            await containers.GetAsync(containerName, ct);
            _logger.LogDebug(
                "deployment container exists account={Account} container={Container}",
                storageAccountId.Name, containerName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var data = new BlobContainerData();
            await containers.CreateOrUpdateAsync(WaitUntil.Completed, containerName, data, ct);
            _logger.LogInformation(
                "deployment container created account={Account} container={Container}",
                storageAccountId.Name, containerName);
        }
    }

    private async Task<string> GetStorageConnectionStringAsync(ResourceIdentifier storageAccountId, CancellationToken ct)
    {
        // Storage account key listing comes back as a Pageable even though
        // there are only ever two keys — enumerate to the first.
        var account = _arm.GetStorageAccountResource(storageAccountId);
        string? primary = null;
        await foreach (var key in account.GetKeysAsync(expand: null, cancellationToken: ct))
        {
            primary = key.Value;
            break;
        }
        if (primary is null)
        {
            throw new InvalidOperationException(
                $"Storage account {storageAccountId.Name} returned no keys.");
        }
        return $"DefaultEndpointsProtocol=https;AccountName={storageAccountId.Name};AccountKey={primary};EndpointSuffix=core.windows.net";
    }

    private static async Task UpdateAppSettingsAsync(
        WebSiteResource site,
        IDictionary<string, string> settings,
        CancellationToken ct)
    {
        // Azure rejects an empty app-settings PUT with a 400 ("The
        // siteAppSettings field is required"). A Static frontend is served as
        // plain files and needs no app settings, so an empty set means "leave
        // the site's settings untouched" rather than pushing an invalid empty
        // payload. (v002 Static provisioning failed here, 2026-06-19.)
        if (settings.Count == 0) return;

        var dict = new AppServiceConfigurationDictionary();
        foreach (var kvp in settings)
        {
            dict.Properties[kvp.Key] = kvp.Value;
        }
        await site.UpdateApplicationSettingsAsync(dict, ct);
    }

    private IDictionary<string, string> BuildSharedAppSettings(
        ResourceNames names, string clerkPublishableKey, string appInsightsConn, string miClientId)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Clerk__PublishableKey"]                 = clerkPublishableKey,
            // Authority is the Clerk Frontend API URL; user-app Functions use
            // it to validate Clerk-issued JWTs without per-app configuration.
            ["Clerk__Authority"]                      = _opts.ClerkAuthority,
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = appInsightsConn,
            ["Cosmos__Endpoint"]                      = _opts.UserdataCosmosEndpoint,
            ["Cosmos__Database"]                      = _opts.UserdataCosmosDatabase,
            ["Cosmos__Container"]                     = names.CosmosContainer,
            ["KeyVault__Uri"]                         = $"https://{_opts.KeyVaultName}.vault.azure.net/",
            ["KeyVault__SecretPrefix"]                = names.KvSecretPrefix,
            // DefaultAzureCredential picks this MI by client id when more than
            // one identity is attached (frontend + Function App both attach
            // their per-app UAMI plus any platform-injected ones).
            ["AZURE_CLIENT_ID"]                       = miClientId,
        };
    }

    private async Task EnsureCosmosContainerAsync(ResourceNames names, CancellationToken ct)
    {
        var accountResourceId = CosmosAccountResourceId();
        var account = _arm.GetCosmosDBAccountResource(accountResourceId);
        var dbResp = await account.GetCosmosDBSqlDatabaseAsync(_opts.UserdataCosmosDatabase, ct);
        var containers = dbResp.Value.GetCosmosDBSqlContainers();

        var resource = new CosmosDBSqlContainerResourceInfo(names.CosmosContainer)
        {
            PartitionKey = new CosmosDBContainerPartitionKey
            {
                Kind = CosmosDBPartitionKind.Hash,
                Paths = { "/id" },
            },
        };
        var content = new CosmosDBSqlContainerCreateOrUpdateContent(_opts.Location, resource);
        await containers.CreateOrUpdateAsync(WaitUntil.Completed, names.CosmosContainer, content, ct);
        _logger.LogInformation("cosmos container ensured account={Acc} db={Db} container={Cont}",
            _opts.UserdataCosmosAccountName, _opts.UserdataCosmosDatabase, names.CosmosContainer);
    }

    private async Task EnsureCosmosRoleAssignmentAsync(Guid principalId, ResourceNames names, CancellationToken ct)
    {
        var accountResourceId = CosmosAccountResourceId();
        var account = _arm.GetCosmosDBAccountResource(accountResourceId);

        var dataPlaneScope =
            $"{accountResourceId}/dbs/{_opts.UserdataCosmosDatabase}/colls/{names.CosmosContainer}";
        var roleDefinitionId = new ResourceIdentifier(
            $"{accountResourceId}/sqlRoleDefinitions/{CosmosDataContributorRoleDefinitionId}");

        var assignmentId = DeterministicGuid($"{principalId}|{dataPlaneScope}");
        var assignments = account.GetCosmosDBSqlRoleAssignments();
        var data = new CosmosDBSqlRoleAssignmentCreateOrUpdateContent
        {
            PrincipalId = principalId,
            RoleDefinitionId = roleDefinitionId,
            Scope = dataPlaneScope,
        };

        try
        {
            await assignments.CreateOrUpdateAsync(WaitUntil.Completed, assignmentId.ToString(), data, ct);
            _logger.LogInformation("cosmos role assignment ensured principal={Principal} scope={Scope}",
                principalId, dataPlaneScope);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogDebug("cosmos role assignment already present principal={Principal}", principalId);
        }
    }

    private async Task EnsureWebsiteContributorAsync(Guid principalId, ResourceIdentifier scope, CancellationToken ct)
    {
        var roleDefId = new ResourceIdentifier(
            $"/subscriptions/{_opts.SubscriptionId}/providers/Microsoft.Authorization/" +
            $"roleDefinitions/{WebsiteContributorRoleDefinitionId}");

        var assignmentId = DeterministicGuid($"{principalId}|{scope}|website-contributor");
        var assignments  = _arm.GetRoleAssignments(scope);

        var content = new RoleAssignmentCreateOrUpdateContent(roleDefId, principalId)
        {
            PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
        };

        try
        {
            await assignments.CreateOrUpdateAsync(WaitUntil.Completed, assignmentId.ToString(), content, ct);
            _logger.LogInformation(
                "website contributor assigned principal={Principal} scope={Scope}",
                principalId, scope);
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.ErrorCode == "RoleAssignmentExists")
        {
            _logger.LogDebug(
                "website contributor already assigned principal={Principal} scope={Scope}",
                principalId, scope);
        }
    }

    private async Task EnsureReaderAsync(Guid principalId, ResourceIdentifier scope, CancellationToken ct)
    {
        var roleDefId = new ResourceIdentifier(
            $"/subscriptions/{_opts.SubscriptionId}/providers/Microsoft.Authorization/" +
            $"roleDefinitions/{ReaderRoleDefinitionId}");

        var assignmentId = DeterministicGuid($"{principalId}|{scope}|reader");
        var assignments  = _arm.GetRoleAssignments(scope);

        var content = new RoleAssignmentCreateOrUpdateContent(roleDefId, principalId)
        {
            PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
        };

        try
        {
            await assignments.CreateOrUpdateAsync(WaitUntil.Completed, assignmentId.ToString(), content, ct);
            _logger.LogInformation("reader assigned principal={Principal} scope={Scope}", principalId, scope);
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.ErrorCode == "RoleAssignmentExists")
        {
            _logger.LogDebug("reader already assigned principal={Principal} scope={Scope}", principalId, scope);
        }
    }

    private async Task EnsureKvSecretsUserRoleAssignmentAsync(Guid principalId, CancellationToken ct)
    {
        // Azure RBAC scopes Key Vault access at the vault, not at the secret.
        // The per-app secret prefix is enforced inside user-app code; vault-
        // level is the smallest scope KV's data-plane RBAC supports. Documented
        // in ADR-0002's Consequences.
        var subscriptionId = _opts.SubscriptionId;
        var kvScopeId = new ResourceIdentifier(
            $"/subscriptions/{subscriptionId}/resourceGroups/{KeyVaultRg()}" +
            $"/providers/Microsoft.KeyVault/vaults/{_opts.KeyVaultName}");
        var roleDefId = new ResourceIdentifier(
            $"/subscriptions/{subscriptionId}/providers/Microsoft.Authorization/" +
            $"roleDefinitions/{KeyVaultSecretsUserRoleDefinitionId}");

        var assignmentId = DeterministicGuid($"{principalId}|{kvScopeId}|kv-secrets-user");
        var assignments = _arm.GetRoleAssignments(kvScopeId);

        var content = new RoleAssignmentCreateOrUpdateContent(roleDefId, principalId)
        {
            PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
        };

        try
        {
            await assignments.CreateOrUpdateAsync(WaitUntil.Completed, assignmentId.ToString(), content, ct);
            _logger.LogInformation("KV secrets user assigned principal={Principal} kv={KV}",
                principalId, _opts.KeyVaultName);
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.ErrorCode == "RoleAssignmentExists")
        {
            _logger.LogDebug("KV secrets user already assigned principal={Principal}", principalId);
        }
    }

    private ResourceIdentifier CosmosAccountResourceId() => new(
        $"/subscriptions/{_opts.SubscriptionId}/resourceGroups/{CosmosRg()}" +
        $"/providers/Microsoft.DocumentDB/databaseAccounts/{_opts.UserdataCosmosAccountName}");

    /// Cosmos account RG: the explicit override when set, otherwise
    /// `ResourceGroup`. The override exists because the shared Cosmos
    /// account lives in the platform RG, not the per-user-app RG.
    private string CosmosRg() => string.IsNullOrWhiteSpace(_opts.UserdataCosmosResourceGroup)
        ? _opts.ResourceGroup
        : _opts.UserdataCosmosResourceGroup;

    /// Key Vault RG: same shape as `CosmosRg()` for the same reason.
    private string KeyVaultRg() => string.IsNullOrWhiteSpace(_opts.KeyVaultResourceGroup)
        ? _opts.ResourceGroup
        : _opts.KeyVaultResourceGroup;

    private DeployedApp SnapshotFrom(WebSiteResource frontendSite, ResourceNames names, string appId, string ownerUserId)
    {
        var host = frontendSite.Data.DefaultHostName;
        var url = string.IsNullOrEmpty(host) ? null : $"https://{host}";
        var now = DateTimeOffset.UtcNow;
        return new DeployedApp(
            AppId: appId,
            OwnerUserId: ownerUserId,
            Status: DeployedAppStatus.Live,
            Subdomain: url,
            ResourceGroupName: _opts.ResourceGroup,
            AppServicePlanName: names.AppServicePlan,
            FrontendWebAppName: names.FrontendWebApp,
            FlexConsumptionPlanName: names.FlexConsumptionPlan,
            ApiFunctionAppName: names.FunctionApp,
            StorageAccountName: names.StorageAccount,
            ManagedIdentityName: names.ManagedIdentity,
            AppInsightsName: names.AppInsights,
            CosmosAccountName: _opts.UserdataCosmosAccountName,
            CosmosContainerName: names.CosmosContainer,
            KeyVaultName: _opts.KeyVaultName,
            KeyVaultSecretPrefix: names.KvSecretPrefix,
            // Single-instance multi-Org: same PK across every user-app.
            ClerkPublishableKey: _opts.ClerkPublishableKeyFallback,
            CreatedAt: now,
            UpdatedAt: now);
    }

    // Mirrors ProvisionStaticWebAppAsync's DeployedApp shape for the read path:
    // a Static app has only the SWA (no plan, no API, no Cosmos, no KV), so
    // every full-stack slot is null and FrontendWebAppName carries the SWA name.
    private DeployedApp SnapshotFromStaticWebsite(StorageAccountResource account, string appId, string ownerUserId)
    {
        var web = account.Data.PrimaryEndpoints?.WebUri?.ToString();
        var url = string.IsNullOrEmpty(web) ? null : web.TrimEnd('/');
        var name = account.Data.Name;
        var now = DateTimeOffset.UtcNow;
        return new DeployedApp(
            AppId: appId,
            OwnerUserId: ownerUserId,
            Status: DeployedAppStatus.Live,
            Subdomain: url,
            ResourceGroupName: _opts.ResourceGroup,
            AppServicePlanName: null,         // static website has no App Service plan
            FrontendWebAppName: name,          // storage account name → deploy.yml target
            FlexConsumptionPlanName: null,
            ApiFunctionAppName: null,
            StorageAccountName: name,
            ManagedIdentityName: null,
            AppInsightsName: null,
            CosmosAccountName: null,
            CosmosContainerName: null,
            KeyVaultName: null,
            KeyVaultSecretPrefix: null,
            ClerkPublishableKey: null,
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static Guid DeterministicGuid(string seed)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.Take(16).ToArray());
    }
}
