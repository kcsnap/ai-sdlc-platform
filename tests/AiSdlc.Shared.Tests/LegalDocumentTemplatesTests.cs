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
        Assert.Contains(all, d => d.Path == "public/privacy-policy.html");
        Assert.Contains(all, d => d.Path == "public/terms-of-service.html");
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
        Assert.Equal(LegalDocumentTemplates.PrivacyPolicyUrl, "/" + LegalDocumentTemplates.PrivacyPolicyPath["public/".Length..]);
        Assert.Equal(LegalDocumentTemplates.TermsOfServiceUrl, "/" + LegalDocumentTemplates.TermsOfServicePath["public/".Length..]);
    }
}
