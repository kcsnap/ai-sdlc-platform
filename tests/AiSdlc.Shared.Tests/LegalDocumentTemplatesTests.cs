using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Shared.Tests;

public sealed class LegalDocumentTemplatesTests
{
    [Fact]
    public void Provides_privacy_and_terms_as_static_public_files()
    {
        var all = LegalDocumentTemplates.All;

        Assert.Equal(2, all.Count);
        // Must land in Vite's publicDir (src/frontend/public/) so the files are served at site root,
        // which is what the AppShell footer links to (#135).
        Assert.Contains(all, d => d.Path == "src/frontend/public/privacy-policy.html");
        Assert.Contains(all, d => d.Path == "src/frontend/public/terms-of-service.html");
        Assert.All(all, d => Assert.False(string.IsNullOrWhiteSpace(d.Content)));
    }

    [Theory]
    [InlineData("Privacy Policy")]
    [InlineData("Terms of Service")]
    public void Each_document_carries_the_not_production_ready_disclaimer(string title)
    {
        var doc = LegalDocumentTemplates.All.Single(d => d.Content.Contains($"<title>{title}</title>"));

        Assert.Contains("not production ready", doc.Content, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reviewed", doc.Content, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before this app goes public", doc.Content, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Link_urls_match_the_committed_file_paths()
    {
        var dir = LegalDocumentTemplates.FrontendPublicDir;
        Assert.Equal(LegalDocumentTemplates.PrivacyPolicyUrl, "/" + LegalDocumentTemplates.PrivacyPolicyPath[dir.Length..]);
        Assert.Equal(LegalDocumentTemplates.TermsOfServiceUrl, "/" + LegalDocumentTemplates.TermsOfServicePath[dir.Length..]);
    }
}
