namespace Yorrixx.Modules.Hosting.Internal;

/// Hosting module configuration per ADR-0002 (F1 frontend Web App) + ADR-0003
/// (Flex Consumption API Function App). All per-user-app resources land in a
/// single shared resource group + region; tenant data lives in a single shared
/// Cosmos serverless account (container per app) and a single shared Key Vault
/// (secret prefix per app).
internal sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// Azure subscription owning every per-user-app resource.
    public string SubscriptionId { get; init; } = "";

    /// Entra ID tenant the per-app deploy SPs live in. Echoed into deploy.yml
    /// so `azure/login` targets the right authority.
    public string TenantId { get; init; } = "";

    /// Client ID of the Yorrixx API's own managed identity. Used by
    /// `EntraDeployIdentityProvisioner` to look up its own SP object ID
    /// so it can be added as owner of newly-created App Registrations —
    /// without this, `Application.ReadWrite.OwnedBy` blocks the
    /// subsequent service-principal create with 403
    /// `Authorization_RequestDenied`.
    public string ApiManagedIdentityClientId { get; init; } = "";

    /// Single shared resource group hosting every user-app's resources.
    public string ResourceGroup { get; init; } = "";

    /// Region for all per-app resources. Originally `uksouth` per ADR-0002,
    /// migrated to `westeurope` on 2026-06-08 after UK South VM/F1 quota
    /// (`Total VMs: 0`) repeatedly blocked F1 App Service Plan creation.
    /// The Yorrixx platform stack (Cosmos, KV, Container App, ACR) remains
    /// in uksouth pending a separate migration.
    public string Location { get; init; } = "westeurope";

    /// Shared Cosmos serverless account; per-app data lives in containers
    /// within `UserdataCosmosDatabase` (ADR-0002: container-per-app within a
    /// single shared account, partition key `/id`).
    public string UserdataCosmosAccountName { get; init; } = "";
    public string UserdataCosmosEndpoint { get; init; } = "";
    public string UserdataCosmosDatabase { get; init; } = "userapps";

    /// Resource group that physically hosts the Cosmos account. Decoupled
    /// from `ResourceGroup` (per-user-app target RG) because the Cosmos
    /// account is shared platform infra and typically lives in the
    /// platform RG (`rg-yorrixx-dev`). Empty → fall back to `ResourceGroup`
    /// for back-compat with envs where they coincide.
    public string UserdataCosmosResourceGroup { get; init; } = "";

    /// Shared Key Vault hosting all user-app secrets. Each app's secrets are
    /// prefixed `app-{appId8}--` and the app's MI gets Key Vault Secrets User
    /// scoped to that prefix only.
    public string KeyVaultName { get; init; } = "";

    /// Resource group that physically hosts the Key Vault. Same rationale
    /// as `UserdataCosmosResourceGroup` — KV is shared platform infra.
    public string KeyVaultResourceGroup { get; init; } = "";

    /// Shared Log Analytics workspace ARM resource ID. Per-app App Insights
    /// components attach to it so logs/metrics land in one queryable place.
    public string AppInsightsWorkspaceId { get; init; } = "";

    /// Clerk publishable key baked into user-app frontend + API app settings.
    /// Single-instance multi-Org model (see ADR-0002 amendment): the
    /// publishable key is instance-wide and shared by every user-app's
    /// frontend; per-app isolation is via Clerk Organization not instance.
    /// Also used as fallback when `IClerkOrgProvisioner` is stubbed.
    public string ClerkPublishableKeyFallback { get; init; } = "";

    /// Clerk Backend API secret key (`sk_test_*` / `sk_live_*`). When set,
    /// HostingModule registers the real `ClerkOrgProvisioner`; otherwise the
    /// stub is used. Sourced from Key Vault in production.
    public string ClerkSecretKey { get; init; } = "";

    /// Clerk Frontend API authority (e.g. `https://clerk.<tenant>.com` or
    /// `https://<subdomain>.clerk.accounts.dev`). Injected into user-app
    /// shared app settings so the user-app Functions API can validate
    /// Clerk-issued JWTs without further configuration.
    public string ClerkAuthority { get; init; } = "";

    /// GitHub org under which user-app repos live. Must match the SourceControl
    /// module's `GitHubAppOptions.OrgSlug`; the per-app deploy SP's federated
    /// credential subject is bound to `repo:{owner}/{repoNamePrefix}-{appId8}`.
    public string UserAppRepoOwner { get; init; } = "yorrixx-apps";

    /// User-app repo name prefix; full repo name is `{prefix}-{appId8}`. Must
    /// match `GitHubAppOptions.RepoNamePrefix`.
    public string UserAppRepoNamePrefix { get; init; } = "user-app";

    /// Default branch in user-app repos. Federated credential subject is
    /// pinned to this branch, so only its workflow runs can mint deploy tokens.
    public string UserAppDefaultBranch { get; init; } = "main";
}
