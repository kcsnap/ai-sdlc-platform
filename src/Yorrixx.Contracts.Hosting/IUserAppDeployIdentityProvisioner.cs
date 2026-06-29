namespace Yorrixx.Contracts.Hosting;

/// Per-user-app Azure deploy identity. Each user-app repo's GitHub Actions
/// workflow authenticates to Azure as this identity via OIDC — no long-lived
/// secrets in the repo, no shared credentials across tenants.
///
/// Architecturally this realises decision #11c (per-app identity boundary):
/// the deploy identity has scoped RBAC only on the per-app resources it owns
/// (its own SWA, Container App, Cosmos container, Key Vault) — never on the
/// control plane or other user-apps.
public sealed record UserAppDeployIdentity(
    string AppId,
    string TenantId,
    string SubscriptionId,
    string ClientId,
    string ApplicationObjectId,
    // Service principal object id — distinct from ApplicationObjectId. This is
    // what Azure RBAC role assignments target (the SP is the security
    // principal that owns tokens minted via the federated credential).
    string ServicePrincipalObjectId,
    DateTimeOffset CreatedAt);

/// Provisions and removes per-user-app Azure AD app registrations + federated
/// credentials. Implementations talk to Microsoft Graph.
///
/// The federated credential's subject is bound to a specific GitHub repo + ref
/// (`repo:{owner}/{repo}:ref:refs/heads/{branch}`) so only that repo's main
/// branch workflows can mint tokens for this identity.
///
/// Idempotent: calling Ensure twice for the same appId yields the same
/// identity. Calling Remove for an appId that doesn't exist is a no-op.
public interface IUserAppDeployIdentityProvisioner
{
    Task<UserAppDeployIdentity> EnsureAsync(
        string appId,
        string repoOwner,
        string repoName,
        string defaultBranch,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string appId, CancellationToken cancellationToken = default);
}
