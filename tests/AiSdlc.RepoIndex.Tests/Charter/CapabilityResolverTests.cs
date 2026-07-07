using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;
using Xunit;
using CharterDoc = Yorrixx.Contracts.Generation.Charter;

namespace AiSdlc.RepoIndex.Tests.Charter;

public sealed class CapabilityResolverTests
{
    private static CharterDoc CharterWith(
        bool needsAuth = false, bool needsPayments = false,
        bool needsEmail = false, bool needsAIApi = false) =>
        TestCharters.Make(auth: needsAuth, payments: needsPayments, email: needsEmail, aiApi: needsAIApi);

    // ── The enforced invariant: NeedsPayments ⟹ Database (§4a) ──────────────────

    [Fact]
    public void Payments_forces_database_on_even_when_derivation_says_no()
    {
        var profile = CapabilityResolver.Resolve(CharterWith(needsPayments: true), databaseDerived: false);

        Assert.True(profile.Database, "Payments must force the database on regardless of the Balanced derivation.");
        Assert.True(profile.Payments);
    }

    [Fact]
    public void Payments_keeps_database_on_when_derivation_also_says_yes()
    {
        var profile = CapabilityResolver.Resolve(CharterWith(needsPayments: true), databaseDerived: true);
        Assert.True(profile.Database);
    }

    // ── The agent-derived axis when no invariant applies ────────────────────────

    [Fact]
    public void No_payments_database_follows_the_derivation_true()
    {
        var profile = CapabilityResolver.Resolve(CharterWith(needsPayments: false), databaseDerived: true);
        Assert.True(profile.Database);
    }

    [Fact]
    public void No_payments_and_no_derived_need_yields_api_only()
    {
        var profile = CapabilityResolver.Resolve(CharterWith(needsPayments: false), databaseDerived: false);

        Assert.False(profile.Database);   // api-only
        Assert.True(profile.Api);         // FullStack always fronts an API
    }

    // ── Class-A flags are mirrored, never invented ──────────────────────────────

    [Fact]
    public void Explicit_flags_are_mirrored_from_the_charter()
    {
        var charter = CharterWith(needsAuth: true, needsPayments: true, needsEmail: true, needsAIApi: true);
        var profile = CapabilityResolver.Resolve(charter, databaseDerived: false);

        Assert.True(profile.Auth);
        Assert.True(profile.Payments);
        Assert.True(profile.Email);
        Assert.True(profile.AIApi);
        Assert.True(profile.Api);
    }

    [Fact]
    public void Api_is_always_present_in_a_fullstack_profile()
    {
        var profile = CapabilityResolver.Resolve(CharterWith(), databaseDerived: false);
        Assert.True(profile.Api);
    }

    // ── Soft gap detection: payments without email (§4b) ────────────────────────

    [Fact]
    public void Payments_without_email_is_flagged_as_a_gap()
    {
        var gaps = CapabilityResolver.DetectGaps(CharterWith(needsPayments: true, needsEmail: false));

        var gap = Assert.Single(gaps);
        Assert.Equal("Email", gap.Capability);
    }

    [Fact]
    public void Payments_with_email_has_no_gap()
    {
        var gaps = CapabilityResolver.DetectGaps(CharterWith(needsPayments: true, needsEmail: true));
        Assert.Empty(gaps);
    }

    [Fact]
    public void No_payments_has_no_email_gap()
    {
        var gaps = CapabilityResolver.DetectGaps(CharterWith(needsPayments: false, needsEmail: false));
        Assert.Empty(gaps);
    }
}
