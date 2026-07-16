using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yorrixx.Contracts.Hosting;
using Yorrixx.Modules.Hosting.Internal;
using Yorrixx.Shared;

namespace Yorrixx.Modules.Hosting;

/// Phase 4 module per ADR-0002 (F1 frontend Web App) + ADR-0003 (Flex
/// Consumption API Function App). Provisions the full per-user-app stack
/// when Hosting:SubscriptionId is set; falls back to stubs otherwise so
/// local dev can boot without Azure permissions.
public sealed class HostingModule : IModuleRegistration
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HostingOptions>(configuration.GetSection(HostingOptions.SectionName));

        // Singleton, thread-safe per Azure SDK guidance.
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton(sp => new ArmClient(sp.GetRequiredService<TokenCredential>()));

        // IHttpClientFactory for HostingService's Cost Management REST query.
        services.AddHttpClient();

        var subscriptionId = configuration[$"{HostingOptions.SectionName}:SubscriptionId"];
        var tenantId       = configuration[$"{HostingOptions.SectionName}:TenantId"];
        var configured     = !string.IsNullOrWhiteSpace(subscriptionId);

        if (configured)
        {
            services.AddSingleton<IHostingService, HostingService>();
        }
        else
        {
            services.AddSingleton<IHostingService, StubHostingService>();
        }

        // Real Entra deploy-identity provisioner needs the API MI to have
        // Graph Application.ReadWrite.OwnedBy granted (see STAGE_1_SETUP.md).
        // Switch to it once Hosting:TenantId is set; stub keeps local dev
        // working without that grant.
        if (configured && !string.IsNullOrWhiteSpace(tenantId))
        {
            services.AddHttpClient<EntraDeployIdentityProvisioner>();
            services.AddSingleton<IUserAppDeployIdentityProvisioner>(sp =>
                sp.GetRequiredService<EntraDeployIdentityProvisioner>());
        }
        else
        {
            services.AddSingleton<IUserAppDeployIdentityProvisioner, StubUserAppDeployIdentityProvisioner>();
        }

        // Real Clerk Backend API provisioner is registered when
        // Hosting:ClerkSecretKey is set (requires a Clerk B2B SaaS plan).
        // Otherwise the stub keeps Go-Live working without a Clerk account.
        var clerkSecretKey = configuration[$"{HostingOptions.SectionName}:ClerkSecretKey"];
        if (!string.IsNullOrWhiteSpace(clerkSecretKey))
        {
            services.AddHttpClient<ClerkOrgProvisioner>();
            services.AddSingleton<IClerkOrgProvisioner>(sp =>
                sp.GetRequiredService<ClerkOrgProvisioner>());
        }
        else
        {
            services.AddSingleton<IClerkOrgProvisioner, StubClerkOrgProvisioner>();
        }
    }
}

internal sealed class StubHostingService : IHostingService
{
    public Task<DeployedApp?> GetAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult<DeployedApp?>(null);

    public Task<DeployedApp> EnsureProvisionedAsync(
        string appId,
        string ownerUserId,
        string appName,
        HostingCapabilities capabilities,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DeployedApp(
            AppId: appId,
            OwnerUserId: ownerUserId,
            Status: DeployedAppStatus.NotProvisioned,
            Subdomain: null,
            ResourceGroupName: null,
            AppServicePlanName: null,
            FrontendWebAppName: null,
            FlexConsumptionPlanName: null,
            ApiFunctionAppName: null,
            StorageAccountName: null,
            ManagedIdentityName: null,
            AppInsightsName: null,
            CosmosAccountName: null,
            CosmosContainerName: null,
            KeyVaultName: null,
            KeyVaultSecretPrefix: null,
            ClerkPublishableKey: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow));

    public Task QuarantineAsync(string appId, string reason, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeprovisionAsync(string appId, string appName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeprovisionByAppIdAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<(long MinorUnits, string Currency)> GetHostingSpendByAppIdAsync(
        string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(long, string)>((0, "GBP"));

    public Task<int> CleanupE2eTestUsersAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

/// Returns deterministic fake values keyed off appId for local dev or
/// environments without the Graph permission grant.
internal sealed class StubUserAppDeployIdentityProvisioner : IUserAppDeployIdentityProvisioner
{
    public Task<UserAppDeployIdentity> EnsureAsync(
        string appId,
        string repoOwner,
        string repoName,
        string defaultBranch,
        long? repoOwnerId = null,
        long? repoId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new UserAppDeployIdentity(
            AppId: appId,
            TenantId: "00000000-0000-0000-0000-000000000000",
            SubscriptionId: "00000000-0000-0000-0000-000000000000",
            ClientId: $"stub-client-{appId}",
            ApplicationObjectId: $"stub-app-{appId}",
            ServicePrincipalObjectId: $"stub-sp-{appId}",
            CreatedAt: DateTimeOffset.UtcNow));

    public Task RemoveAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// Returns deterministic fake Clerk Org metadata. Real Clerk Backend API
/// implementation requires a Clerk B2B SaaS plan (~$25/mo, see ADR-0002
/// Consequences) and a separate secret-key ticket on the API side.
internal sealed class StubClerkOrgProvisioner : IClerkOrgProvisioner
{
    public Task<UserAppClerkOrg> EnsureAsync(
        string appId,
        string appName,
        string builderUserId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new UserAppClerkOrg(
            AppId: appId,
            OrgId: $"stub-org-{appId}",
            PublishableKey: "",
            CreatedAt: DateTimeOffset.UtcNow));

    public Task RemoveAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<int> CleanupE2eTestUsersAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
