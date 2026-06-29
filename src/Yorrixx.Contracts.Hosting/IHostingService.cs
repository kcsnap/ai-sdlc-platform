namespace Yorrixx.Contracts.Hosting;

/// Per-app deployed environment per ADR-0002 (F1 frontend Web App) + ADR-0003
/// (Flex Consumption API Function App). One App Service Plan + one Flex Plan
/// per user-app; storage account dedicated to the Function App runtime; UAMI
/// scoped to the per-app Cosmos container + KV secret prefix.
public sealed record DeployedApp(
    string AppId,
    string OwnerUserId,
    DeployedAppStatus Status,

    // Frontend Web App's azurewebsites.net URL once provisioning completes —
    // surfaced to the user as the "hosted at" link until *.apps.yorrixx.io
    // gets wired up (B1 upgrade trigger per ADR-0002).
    string? Subdomain,

    // Shared resource group hosting all per-app resources.
    string? ResourceGroupName,

    // ADR-0002: one F1 Plan per user-app for the frontend Web App only.
    string? AppServicePlanName,
    string? FrontendWebAppName,

    // ADR-0003: one Flex Consumption plan per user-app for the API Function App.
    string? FlexConsumptionPlanName,
    string? ApiFunctionAppName,
    string? StorageAccountName,

    // Per-app UAMI, App Insights component.
    string? ManagedIdentityName,
    string? AppInsightsName,

    // Shared serverless Cosmos account; per-app container.
    string? CosmosAccountName,
    string? CosmosContainerName,

    // Shared `kv-yorrixx-dev` and the per-app secret prefix (e.g. `app-b80683eb--`).
    string? KeyVaultName,
    string? KeyVaultSecretPrefix,

    // Clerk publishable key (`pk_test_*` / `pk_live_*`). Single-instance
    // multi-Org model: every user-app's frontend uses the same instance-wide
    // key (per-app isolation is via Clerk Organization). Baked into the
    // user-app's deploy.yml as a Vite build-time env var so the SPA can
    // mount ClerkProvider without a runtime config round-trip.
    string? ClerkPublishableKey,

    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum DeployedAppStatus
{
    NotProvisioned, // App row exists in Yorrixx but no runtime infra has been created yet
    Provisioning,
    Live,
    Building,        // a new revision is being built but the previous one still serves
    LastUpdateFailed,
    Quarantined,     // see arch decision #10 — 3 consecutive failures locks the app
}

public interface IHostingService
{
    Task<DeployedApp?> GetAsync(string appId, CancellationToken cancellationToken = default);

    /// Provisions (idempotently) the per-user-app stack. For a full-stack app
    /// (ADR-0002 + ADR-0003): F1 plan + frontend Web App + Flex Consumption plan
    /// + Function App + storage + UAMI + App Insights + Cosmos container + KV
    /// secret RBAC + deploy SP + Clerk Org. `appName` feeds the slug component of
    /// resource names (`{kind}-{appNameSlug8}-{appId8}`) — pass the charter app
    /// name; fallback to `app` if blank.
    ///
    /// <paramref name="capabilities"/> composes the shell (fullstack-capability-
    /// derivation): <see cref="ProvisionPlan.From"/> resolves which resources are
    /// created. **No capability ⟹ a Static site** — an Azure Static Web App (no
    /// App Service plan, UAMI, App Insights, storage, Clerk, Flex/Function App,
    /// Cosmos, or KV); its name rides in DeployedApp.FrontendWebAppName and the
    /// deploy SP gets Contributor on the SWA + Reader on the RG. Auth ⟹ Clerk Org;
    /// Database (or Payments) ⟹ Cosmos + data-plane RBAC; any capability ⟹ the API
    /// tier (and the full-stack path: RG, UAMI, App Insights, storage, F1 plan,
    /// frontend Web App, deploy SP + frontend RBAC). The returned DeployedApp's
    /// API/Cosmos/Clerk fields are null for the slots not provisioned.
    Task<DeployedApp> EnsureProvisionedAsync(
        string appId,
        string ownerUserId,
        string appName,
        HostingCapabilities capabilities,
        CancellationToken cancellationToken = default);

    Task QuarantineAsync(string appId, string reason, CancellationToken cancellationToken = default);

    /// Tears down the app's runtime infrastructure when it is deleted: the
    /// per-app Azure resources (frontend Web App, API Function App, both App
    /// Service plans, storage account, UAMI, App Insights), the per-app Cosmos
    /// container, the Clerk Org, and the deploy service principal / app
    /// registration. Replaces the manual `sweep-userapp-orphans.ps1` for the
    /// cost-bearing resources. Best-effort and idempotent: already-deleted
    /// resources are skipped, and a failure on any one resource is logged and
    /// does not stop the rest (the sweep script stays as a backstop). Key Vault
    /// secrets (`app-{id8}--*`, negligible cost) are left to the manual sweep —
    /// deleting them needs a data-plane client this service doesn't hold.
    Task DeprovisionAsync(string appId, string appName, CancellationToken cancellationToken = default);

    /// Deletes Clerk users created by the seeded e2e auth spec for this app
    /// (see IClerkOrgProvisioner.CleanupE2eTestUsersAsync). Called by release
    /// verification after the UI-test check, pass or fail. Returns the number
    /// of users deleted.
    Task<int> CleanupE2eTestUsersAsync(string appId, CancellationToken cancellationToken = default);
}
