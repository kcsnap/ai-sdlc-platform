using AiSdlc.ModelProviders;
using Xunit;

namespace AiSdlc.ModelProviders.Tests;

public sealed class ModelProviderOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage-no-equals")]
    [InlineData("=missing-agent")]   // empty key
    public void ParseOverrides_returns_empty_for_blank_or_malformed(string? spec)
    {
        Assert.Empty(ModelProviderOptions.ParseOverrides(spec));
    }

    [Fact]
    public void ParseOverrides_parses_multiple_entries_with_spaces_and_slashes()
    {
        var map = ModelProviderOptions.ParseOverrides(
            "Code Implementer=claude-opus-4-8;UX / Accessibility Reviewer=claude-sonnet-4-6");

        Assert.Equal(2, map.Count);
        Assert.Equal("claude-opus-4-8", map["Code Implementer"]);
        Assert.Equal("claude-sonnet-4-6", map["UX / Accessibility Reviewer"]);
    }

    [Fact]
    public void ParseOverrides_trims_whitespace_and_is_case_insensitive()
    {
        var map = ModelProviderOptions.ParseOverrides("  Code Implementer = claude-opus-4-8 ; ");

        Assert.Single(map);
        Assert.Equal("claude-opus-4-8", map["code implementer"]); // case-insensitive lookup
    }

    [Fact]
    public void ParseOverrides_skips_entries_missing_a_model()
    {
        var map = ModelProviderOptions.ParseOverrides("Code Implementer=;Architect=claude-opus-4-8");

        Assert.Single(map);
        Assert.False(map.ContainsKey("Code Implementer"));
        Assert.Equal("claude-opus-4-8", map["Architect"]);
    }

    [Fact]
    public void Default_overrides_map_is_empty()
    {
        var options = new ModelProviderOptions { ProviderName = "Anthropic", ModelName = "claude-haiku-4-5-20251001" };
        Assert.Empty(options.ModelOverridesByAgent);
    }
}
