namespace Yorrixx.Provisioner.Contracts;

// Phase-1 provisioner⇄platform contract (responsibility-split-phase1-provisioner-contract.md,
// ratified 2026-06-24). The platform sends a DECLARATIVE spec (WHAT the app needs); the provisioner
// owns HOW (ARM topology, naming, SKUs, RBAC, Clerk-org mechanics, rollback). These types are kept in
// a standalone, dependency-free library so they transfer cleanly when the provisioner's ownership moves
// to the platform (build-then-transfer).

/// Call 1 — platform → provisioner. Idempotent on <see cref="BuildId"/>.
public sealed record ProvisionSpec(
    string AppId,
    string BuildId,
    string Env,                       // "dev" (default); first-class so prod/multi-region is config, not contract change
    string Region,                    // "northeurope" (default)
    string StackProfile,              // "Static" | "FullStack" — deterministic, platform-derived
    ProvisionCapabilities Capabilities,
    ProvisionRepo Repo,
    // Optional (additive, G5): Clerk user id of the app owner — becomes created_by on the Clerk org.
    // Null/empty ⇒ the org is created without a creator (test/ownerless builds).
    string? OwnerUserId = null,
    // Optional (additive, G5): app display name — feeds the resource-name slug + Clerk org name.
    // Null/empty ⇒ the provisioner falls back to Repo.Name (pre-G5 behaviour).
    string? AppName = null);

/// Resolved capability axes. The provisioner maps these to a resource topology
/// (e.g. Cosmos iff Database, Clerk org iff Auth, Functions API iff an API is implied).
public sealed record ProvisionCapabilities(
    bool Auth,
    bool Database,
    bool Payments,
    bool Email,
    bool AiApi);

// OwnerId/RepoId (additive, F5): GitHub's OIDC sub claim now embeds immutable ids
// (repo:{owner}@{ownerId}/{repo}@{repoId}:ref:...); the provisioner pins a second federated
// credential in that format when they are present. Nullable so older senders stay wire-compatible.
public sealed record ProvisionRepo(string Owner, string Name, string DefaultBranch, long? OwnerId = null, long? RepoId = null);

/// 202 response to Call 1.
public sealed record ProvisionAccepted(string ProvisionId, string Status);

/// Call 2 — provisioner → platform callback (also the body of the GET /provision/{buildId} poll fallback).
public sealed record ProvisionResult(
    string AppId,
    string BuildId,
    string Outcome,                   // "provisioned" | "failed"
    IReadOnlyList<ProvisionedResource> Resources,
    string? HostedUrl,
    DeployIdentitySpec? Deploy,
    ClerkResult? Clerk,
    string? DeployYaml,               // the canonical .github/workflows/deploy.yml to commit (static vs full-stack variant resolved); the platform commits it verbatim — no re-implementation/drift
    string? Detail);                  // cause on failure

public sealed record ProvisionedResource(
    string Kind,                      // webapp | functionapp | cosmos | storage | identity | …
    string Name,
    string ResourceId);

/// Deploy auth = repo-federated OIDC (confirmed: EntraDeployIdentityProvisioner mints an App
/// registration + SP + federated credential bound to repo:{owner}/{repo}:ref:refs/heads/{branch}).
/// Only the non-secret client/tenant/subscription IDs cross the wire; the platform writes them as repo
/// variables and authors deploy.yml with azure/login@v2. No publish profile, no secret. (NB the deploy
/// identity is an App registration, not a user-assigned managed identity.)
public sealed record DeployIdentitySpec(
    string Method,                    // "oidc-federated"
    string ClientId,
    string TenantId,
    string SubscriptionId);

/// Returned only when Capabilities.Auth is true. Single Clerk instance, per-app isolation = Organization;
/// the secret key is instance-wide/shared (SecretKeyVaultRef points at the shared instance secret), the
/// publishable key is the instance key used with the per-app Org.
public sealed record ClerkResult(
    string PublishableKey,
    string? SecretKeyVaultRef);

/// Call 3 — platform → provisioner (delete flow). Tears down everything tagged with the appId,
/// self-rolls-back on partial failure.
public sealed record DeprovisionRequest(string AppId);

public sealed record DeprovisionResult(
    string Outcome,                   // "deprovisioned" | "failed"
    string? Detail);

/// Call 4 — GET /spend?appId=… → provisioner reads Cost-Management by the appId tag. The platform relays
/// this to Yorrixx as /spend {kind:"hosting"}; the provisioner never reports to Yorrixx directly.
public sealed record HostingSpend(
    string Currency,                  // e.g. "GBP"
    long MonthToDateMinor,
    DateTimeOffset AsOf);

/// Mandatory tags stamped on every provisioned resource — power Cost-Management-by-appId + clean,
/// unambiguous deprovision. (managedBy is constant; env mirrors the spec.)
public static class ProvisionTags
{
    public const string AppId = "appId";
    public const string BuildId = "buildId";
    public const string ManagedBy = "managedBy";
    public const string ManagedByValue = "ai-sdlc";
    public const string Env = "env";
}
