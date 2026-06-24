using AiSdlc.RepoIndex.Charter;
using Xunit;

namespace AiSdlc.RepoIndex.Tests.Charter;

public sealed class StackProfileResolverTests
{
    private static AiSdlc.RepoIndex.Charter.Charter WithConstraints(
        bool auth = false, bool email = false, bool payments = false, bool aiApi = false, bool persistence = false) =>
        new()
        {
            Constraints = new CharterConstraints
            {
                NeedsAuth = auth, NeedsEmail = email, NeedsPayments = payments,
                NeedsAIApi = aiApi, NeedsPersistence = persistence
            }
        };

    [Fact]
    public void All_backend_flags_off_is_Static()
    {
        Assert.Equal(StackProfile.Static, StackProfileResolver.Resolve(WithConstraints()));
        Assert.Equal("Static", StackProfileResolver.ResolveName(WithConstraints()));
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
        Assert.Equal(StackProfile.FullStack, StackProfileResolver.Resolve(charter));
        Assert.Equal("FullStack", StackProfileResolver.ResolveName(charter));
    }
}
