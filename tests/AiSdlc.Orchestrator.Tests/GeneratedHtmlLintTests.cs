using AiSdlc.Orchestrator.Builds;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

/// D8: fresh-w2-florist committed three feature icons as entity-escaped SVG — visible tag soup in
/// the browser, green everywhere in our gates. The committed page is pinned as the must-FAIL
/// fixture for BOTH new gates; real green pages must pass.
public sealed class GeneratedHtmlLintTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    // ── Gate 2: pre-commit AngleSharp lint ─────────────────────────────────

    [Fact]
    public void Lint_fails_the_committed_florist_page()
    {
        var violations = GeneratedHtmlLint.Scan(Fixture("fresh-w2-florist-escaped.html"));

        Assert.True(violations.Count >= 3); // three escaped feature icons
        Assert.Contains(violations, v => v.Excerpt.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("my-crochets-live.html")]   // live green page (My Crochets, stc50cb424)
    [InlineData("ramp-w1-florist.html")]    // live green page (F4 fixture)
    public void Lint_passes_real_green_pages(string fixture)
        => Assert.Empty(GeneratedHtmlLint.Scan(Fixture(fixture)));

    [Fact]
    public void Lint_allows_markup_text_inside_code_and_pre()
        => Assert.Empty(GeneratedHtmlLint.Scan(
            "<html><body><pre>&lt;svg viewBox=&quot;0 0 1 1&quot;&gt;</pre><code>&lt;div&gt;</code></body></html>"));

    [Fact]
    public void Rejected_generated_html_is_filtered_from_commits()
    {
        var soup  = new FileChange("index.html", Fixture("fresh-w2-florist-escaped.html"));
        var clean = new FileChange("index.html", Fixture("my-crochets-live.html"));
        var css   = new FileChange("styles.css", ".x { content: '&lt;svg'; }"); // non-HTML: never linted

        Assert.True(GeneratedHtmlLint.IsRejectedGeneratedHtml(soup));
        Assert.False(GeneratedHtmlLint.IsRejectedGeneratedHtml(clean));
        Assert.False(GeneratedHtmlLint.IsRejectedGeneratedHtml(css));
    }

    // ── Gate 1: verify 6th check (no-escaped-markup) ───────────────────────

    [Fact]
    public void Verify_check_fails_the_florist_page_and_passes_green_pages()
    {
        Assert.True(BuildActivityFunctions.HasEscapedMarkupInVisibleText(Fixture("fresh-w2-florist-escaped.html")));
        Assert.False(BuildActivityFunctions.HasEscapedMarkupInVisibleText(Fixture("my-crochets-live.html")));
        Assert.False(BuildActivityFunctions.HasEscapedMarkupInVisibleText(Fixture("ramp-w1-florist.html")));
    }

    [Fact]
    public void Verify_check_ignores_code_blocks_and_catches_double_escaping()
    {
        Assert.False(BuildActivityFunctions.HasEscapedMarkupInVisibleText(
            "<pre>&lt;svg&gt;</pre><p>plain copy</p>"));
        Assert.True(BuildActivityFunctions.HasEscapedMarkupInVisibleText(
            "<p>&amp;lt;svg width=&amp;quot;48&amp;quot;</p>")); // double-escaped renders as &lt;svg…
    }

    [Fact]
    public void AssembleVerification_fails_outcome_on_escaped_markup()
    {
        var v = BuildActivityFunctions.AssembleVerification(
            "success", 200, isStatic: true,
            pageHtml: Fixture("fresh-w2-florist-escaped.html"), appName: "Fresh Florist");

        Assert.Equal("failed", v.Outcome);
        Assert.Equal("fail", Assert.Single(v.Checks, c => c.CheckId == "no-escaped-markup").Status);
        Assert.Equal(6, v.Checks.Count);
    }
}
