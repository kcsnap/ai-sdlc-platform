namespace Yorrixx.Contracts.Hosting;

/// The resolved capability axes an app needs, passed to
/// <see cref="IHostingService.EnsureProvisionedAsync"/>. Generalises the old
/// binary `isStatic` flag (stack-profiles-static-first → fullstack-capability-
/// derivation): a FullStack app is a composition of capability slots, not a
/// monolith. The platform's Architect derives these; the provisioner maps them
/// to a concrete resource topology via <see cref="ProvisionPlan"/>.
///
/// Class-A (explicit) capabilities only. Payments/Email/AiApi need no per-app
/// Azure resource of their own today (Stripe/SendGrid are platform-level shared
/// services; AiApi is a runtime call) — they only imply that a server-side API
/// exists. Persistence (Database → Cosmos) and Auth (→ Clerk org) are the axes
/// that actually gate a per-app resource.
public sealed record HostingCapabilities(
    bool Auth,
    bool Database,
    bool Payments,
    bool Email,
    bool AiApi)
{
    /// A FullStack app implies a server-side API (.NET Functions) whenever any
    /// capability is present — auth needs token validation, a DB needs an API
    /// in front of it, payments/email/AI all run server-side. No capability at
    /// all ⟹ a Static site (frontend only).
    public bool NeedsApi => Auth || Database || Payments || Email || AiApi;

    /// A pure Static site — no API, no data, no integrations.
    public static readonly HostingCapabilities StaticSite = new(false, false, false, false, false);
}

/// The concrete provisioning decision derived from <see cref="HostingCapabilities"/>:
/// which resource groups to create. Pure + deterministic so it is unit-tested
/// directly (the composition logic that had no coverage when the D2 static-skip
/// bugs slipped through). <see cref="IHostingService"/> consumes it to gate its
/// (un-unit-testable) ARM calls.
public sealed record ProvisionPlan(
    bool Frontend,          // always — every app has a web frontend (static files or the SPA)
    bool Api,               // .NET Functions app + Flex plan + deploy container + function-scope RBAC + KV RBAC
    bool Cosmos,            // per-app Cosmos container + data-plane RBAC
    bool ClerkOrg)          // per-app Clerk Organization
{
    public static ProvisionPlan From(HostingCapabilities c) => new(
        Frontend: true,
        Api: c.NeedsApi,
        // Hard invariant (fullstack-capability-derivation §4a): Payments ⟹ Database.
        // Defence-in-depth — the platform resolver already forces it, but never
        // provision payments without the datastore that persists orders.
        Cosmos: c.Database || c.Payments,
        ClerkOrg: c.Auth);
}
