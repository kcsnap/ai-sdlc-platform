using Yorrixx.Contracts.Hosting;
using Yorrixx.DeployTemplate;
using Yorrixx.Provisioner.Contracts;

namespace Yorrixx.Provisioner.Internal;

/// Pure mapping between the platform-facing provisioner contract and the
/// Hosting module's types. Kept side-effect-free so it is unit-tested directly.
public static class ProvisionMapper
{
    /// Declarative capability axes from the spec → the Hosting module's
    /// capability set (1:1; ProvisionPlan then derives the resource topology).
    public static HostingCapabilities ToCapabilities(ProvisionCapabilities c) =>
        new(Auth: c.Auth, Database: c.Database, Payments: c.Payments, Email: c.Email, AiApi: c.AiApi);

    /// Builds the success provision-result from what HostingService provisioned
    /// + the (idempotently re-fetched) deploy identity. Only non-null slots are
    /// reported as resources; deploy auth is always repo-federated OIDC.
    public static ProvisionResult ToResult(
        ProvisionSpec spec,
        DeployedApp app,
        UserAppDeployIdentity identity,
        string subscriptionId,
        string resourceGroup,
        string? apiBaseUrl = null,
        string? formSharedToken = null)
    {
        var resources = new List<ProvisionedResource>();

        void AddWebScoped(string? name, string kind, string provider)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                resources.Add(new ProvisionedResource(kind, name,
                    $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/{provider}/{name}"));
            }
        }

        AddWebScoped(app.FrontendWebAppName, "webapp", "Microsoft.Web/sites");
        AddWebScoped(app.ApiFunctionAppName, "functionapp", "Microsoft.Web/sites");
        AddWebScoped(app.StorageAccountName, "storage", "Microsoft.Storage/storageAccounts");
        AddWebScoped(app.ManagedIdentityName, "identity", "Microsoft.ManagedIdentity/userAssignedIdentities");
        AddWebScoped(app.AppInsightsName, "appinsights", "Microsoft.Insights/components");
        // Cosmos container lives in the shared account (a nested resourceId the
        // provisioner doesn't own here) — report the container name only.
        if (!string.IsNullOrWhiteSpace(app.CosmosContainerName))
            resources.Add(new ProvisionedResource("cosmos", app.CosmosContainerName!, app.CosmosContainerName!));

        // No API Function App ⟺ a Static (frontend-only) site → the static
        // deploy.yml variant; otherwise the full-stack (Vite + Functions) one.
        var isStatic = string.IsNullOrWhiteSpace(app.ApiFunctionAppName);
        var deployYaml = DeployWorkflowTemplate.Render(
            repoOwner: spec.Repo.Owner,
            repoName: spec.Repo.Name,
            tenantId: identity.TenantId,
            clientId: identity.ClientId,
            subscriptionId: identity.SubscriptionId,
            resourceGroup: string.IsNullOrWhiteSpace(resourceGroup) ? "rg" : resourceGroup,
            frontendWebAppName: app.FrontendWebAppName ?? "",
            apiFunctionAppName: app.ApiFunctionAppName ?? "",
            defaultBranch: spec.Repo.DefaultBranch,
            clerkPublishableKey: app.ClerkPublishableKey,
            isStatic: isStatic,
            apiBaseUrl: apiBaseUrl,
            formSharedToken: formSharedToken);

        return new ProvisionResult(
            AppId: spec.AppId,
            BuildId: spec.BuildId,
            Outcome: "provisioned",
            Resources: resources,
            HostedUrl: app.Subdomain,
            Deploy: new DeployIdentitySpec(
                Method: "oidc-federated",
                ClientId: identity.ClientId,
                TenantId: identity.TenantId,
                SubscriptionId: identity.SubscriptionId),
            Clerk: string.IsNullOrWhiteSpace(app.ClerkPublishableKey)
                ? null
                : new ClerkResult(app.ClerkPublishableKey!, SecretKeyVaultRef: null),
            DeployYaml: deployYaml,
            Detail: null);
    }

    public static ProvisionResult Failed(ProvisionSpec spec, string detail) =>
        new(spec.AppId, spec.BuildId, Outcome: "failed",
            Resources: [], HostedUrl: null, Deploy: null, Clerk: null, DeployYaml: null, Detail: detail);
}
