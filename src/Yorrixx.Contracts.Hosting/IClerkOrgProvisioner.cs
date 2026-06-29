namespace Yorrixx.Contracts.Hosting;

/// Per-user-app Clerk Organization within the shared Yorrixx-owned Clerk
/// instance per ADR-0002: each user-app gets its own Org for per-app
/// branding + role boundaries; builders are added as Org admins; cross-app
/// first visits require explicit consent.
///
/// The publishable key is per-Org and is what gets injected into the
/// user-app's frontend Web App + Function App settings so its sign-in flow
/// renders this specific Org's branding.
public sealed record UserAppClerkOrg(
    string AppId,
    string OrgId,
    string PublishableKey,
    DateTimeOffset CreatedAt);

/// Creates / removes per-user-app Clerk Orgs. Real implementation talks to
/// Clerk's Backend API (Organizations + Memberships endpoints) under the
/// Yorrixx instance's secret key. Stub implementation returns deterministic
/// fake values so the Hosting flow compiles + runs without a Clerk B2B SaaS
/// plan attached (which Orgs require — see ADR-0002 Consequences).
///
/// Idempotent: re-running Ensure for the same appId returns the existing Org.
public interface IClerkOrgProvisioner
{
    Task<UserAppClerkOrg> EnsureAsync(
        string appId,
        string appName,
        string builderUserId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string appId, CancellationToken cancellationToken = default);

    /// Deletes Clerk users created by the seeded e2e auth spec (emails
    /// prefixed `e2e-{appId8}-` and carrying `+clerk_test`). Keeps the app's
    /// Org under the free-tier member cap as verification runs accumulate.
    /// Returns the number of users deleted.
    Task<int> CleanupE2eTestUsersAsync(string appId, CancellationToken cancellationToken = default);
}
