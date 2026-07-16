using Yorrixx.Contracts.Hosting;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

/// F5: Entra federated credentials are EXACT-match on the OIDC subject. GitHub's default sub claim
/// switched to an immutable-id form for yorrixx-apps between 2026-07-13 and 2026-07-16, and the
/// classic-only credential failed token exchange (AADSTS700213, fresh-w1-bikeshop). Both formats
/// are pinned here byte-for-byte — the immutable one against the LIVE token from the bikeshop probe.
public sealed class FederatedSubjectsTests
{
    [Fact]
    public void Classic_subject_matches_the_original_format()
        => Assert.Equal(
            "repo:yorrixx-apps/user-app-c11f75d3:ref:refs/heads/main",
            FederatedSubjects.Classic("yorrixx-apps", "user-app-c11f75d3", "main"));

    [Fact]
    public void Immutable_subject_matches_the_live_github_token_byte_for_byte()
        => Assert.Equal(
            "repo:yorrixx-apps@289196324/user-app-c11f75d3@1303218647:ref:refs/heads/main",
            FederatedSubjects.Immutable("yorrixx-apps", 289196324, "user-app-c11f75d3", 1303218647, "main"));
}
