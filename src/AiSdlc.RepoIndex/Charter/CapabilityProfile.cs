
namespace AiSdlc.RepoIndex.Charter;

// Inside this namespace the simple name "Charter" binds to the namespace itself, so the contract-package
// import must live INSIDE the namespace body (inner-scope usings win the lookup).
using Yorrixx.Contracts.Generation;

/// <summary>
/// The resolved set of backend capabilities for a FullStack app, fixed before build from the explicit
/// charter flags plus the Architect's Balanced "does it need persistence?" judgment. Drives the
/// composable scaffold and the Code Implementer contract.
/// See docs/roadmap/fullstack-capability-derivation.md.
/// </summary>
public sealed record CapabilityProfile
{
    /// <summary>FullStack always fronts an API; the real fork is api-only vs api+db (<see cref="Database"/>).</summary>
    public bool Api { get; init; }

    /// <summary>Cosmos persistence. Agent-derived (Balanced), but forced on by hard invariants (e.g. payments).</summary>
    public bool Database { get; init; }

    public bool Auth { get; init; }
    public bool Payments { get; init; }
    public bool Email { get; init; }
    public bool AIApi { get; init; }
}

/// <summary>A capability the brief/flags suggest is missing — surfaced for the human, never auto-applied.</summary>
public sealed record CapabilityGap(string Capability, string Reason);

/// <summary>
/// Resolves the <see cref="CapabilityProfile"/> from a charter. Class-A capabilities (auth, payments,
/// email, AI) are explicit-only — mirrored straight from the wizard flags. The persistence axis is
/// agent-derived, then hard invariants are applied (§4a): a payments app MUST persist, so payments
/// forces the database on regardless of the Balanced derivation or the brief.
/// </summary>
public static class CapabilityResolver
{
    /// <param name="charter">The app charter (explicit wizard constraints).</param>
    /// <param name="databaseDerived">
    /// The Architect's Balanced judgment on whether the app needs persistence. Overridden to true when
    /// an invariant forces the database on.
    /// </param>
    public static CapabilityProfile Resolve(Charter charter, bool databaseDerived)
    {
        ArgumentNullException.ThrowIfNull(charter);
        var c = charter.Constraints;

        // Hard invariant (§4a): NeedsPayments ⟹ Database. A store with no datastore is incoherent, so
        // payments forces persistence on — this wins over the Balanced derivation and is NOT a flaggable
        // gap. Database ⟹ API, and FullStack always fronts an API, so Api is unconditionally present here.
        var database = databaseDerived || c.NeedsPayments;

        return new CapabilityProfile
        {
            Api      = true,
            Database = database,
            Auth     = c.NeedsAuth,
            Payments = c.NeedsPayments,
            Email    = c.NeedsEmail,
            AIApi    = c.NeedsAIApi,
        };
    }

    /// <summary>
    /// Soft cross-checks (§4b): an explicit capability that implies ANOTHER explicit (explicit-only)
    /// capability that's off. These are honored-but-flagged — surfaced at the Product Owner gate, never
    /// auto-added. (Implications that land on the agent-derived persistence axis are enforced in
    /// <see cref="Resolve"/>, not flagged here.)
    /// </summary>
    public static IReadOnlyList<CapabilityGap> DetectGaps(Charter charter)
    {
        ArgumentNullException.ThrowIfNull(charter);
        var c = charter.Constraints;
        var gaps = new List<CapabilityGap>();

        if (c.NeedsPayments && !c.NeedsEmail)
            gaps.Add(new CapabilityGap(
                "Email",
                "Payments are enabled but email is off — receipts and order confirmations usually need " +
                "email. Email is explicit-only, so this is not auto-added; confirm at review."));

        return gaps;
    }
}
