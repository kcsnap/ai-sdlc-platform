using Yorrixx.Contracts.Generation;
using Xunit;

namespace AiSdlc.RepoIndex.Tests.Charter;

/// <summary>
/// Pins the Phase-0-locked deterministic Static-vs-FullStack gate, now single-sourced from the
/// Yorrixx.Contracts.Charter package (StackProfiles.Resolve) — the platform's hand-copied rule is retired.
/// Doubles as drift detection: a package change to the rule fails CI here.
/// </summary>
public sealed class StackProfileResolverTests
{
    private static Yorrixx.Contracts.Generation.Charter WithConstraints(
        bool auth = false, bool email = false, bool payments = false, bool aiApi = false, bool persistence = false) =>
        TestCharters.Make(auth: auth, email: email, payments: payments, aiApi: aiApi, persistence: persistence);

    [Fact]
    public void All_backend_flags_off_is_Static()
    {
        Assert.Equal(StackProfile.Static, StackProfiles.Resolve(WithConstraints()));
        Assert.Equal("Static", StackProfiles.Resolve(WithConstraints()).ToString());
    }

    [Theory]
    [InlineData(true,  false, false, false, false)]   // auth
    [InlineData(false, true,  false, false, false)]   // email
    [InlineData(false, false, true,  false, false)]   // payments
    [InlineData(false, false, false, true,  false)]   // aiApi
    [InlineData(false, false, false, false, true)]    // persistence
    [InlineData(true,  false, false, false, true)]    // combo
    public void Any_backend_need_is_FullStack(bool auth, bool email, bool payments, bool aiApi, bool persistence)
    {
        var charter = WithConstraints(auth, email, payments, aiApi, persistence);
        Assert.Equal(StackProfile.FullStack, StackProfiles.Resolve(charter));
        Assert.Equal("FullStack", StackProfiles.Resolve(charter).ToString());
    }

    [Fact]
    public void Constraints_overload_agrees_with_the_charter_overload()
    {
        var charter = WithConstraints(auth: true);
        Assert.Equal(StackProfiles.Resolve(charter), StackProfiles.Resolve(charter.Constraints));
    }
}
