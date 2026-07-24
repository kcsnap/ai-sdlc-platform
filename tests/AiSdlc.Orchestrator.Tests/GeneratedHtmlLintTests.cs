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

    // D17 sharpened this pin: these review-passed live pages are SOUP-clean, but both genuinely carry
    // dead in-page anchors (the platform-wide dead-anchor finding) — the new anchor dimension must
    // flag exactly those and nothing else.
    [Theory]
    [InlineData("my-crochets-live.html", "#about,#gallery")]  // live green page (My Crochets, stc50cb424)
    [InlineData("ramp-w1-florist.html", "#gallery")]          // live green page (F4 fixture)
    public void Real_green_pages_are_soup_clean_but_carry_their_known_dead_anchors(string fixture, string deadCsv)
    {
        var violations = GeneratedHtmlLint.Scan(Fixture(fixture));

        Assert.All(violations, v => Assert.Contains("dead in-page anchor", v.Excerpt)); // no soup
        Assert.Equal(deadCsv.Split(',').Order(),
            violations.Select(v => v.Excerpt.Split(' ')[3]).Order()); // exactly the known dead links
    }

    [Fact]
    public void Lint_allows_markup_text_inside_code_and_pre()
        => Assert.Empty(GeneratedHtmlLint.Scan(
            "<html><body><pre>&lt;svg viewBox=&quot;0 0 1 1&quot;&gt;</pre><code>&lt;div&gt;</code></body></html>"));

    [Fact]
    public void Rejected_generated_html_is_filtered_from_commits()
    {
        var soup  = new FileChange("index.html", Fixture("fresh-w2-florist-escaped.html"));
        // Clean = soup-free AND anchor-resolving (D17: my-crochets-live no longer qualifies — it
        // carries real dead anchors and must now be rejected too).
        var clean = new FileChange("index.html",
            "<nav><a href=\"#hero\">Top</a></nav><section id=\"hero\">h</section>");
        var anchorDirty = new FileChange("index.html", Fixture("my-crochets-live.html"));
        var css   = new FileChange("styles.css", ".x { content: '&lt;svg'; }"); // non-HTML: never linted

        Assert.True(GeneratedHtmlLint.IsRejectedGeneratedHtml(soup));
        Assert.False(GeneratedHtmlLint.IsRejectedGeneratedHtml(clean));
        Assert.True(GeneratedHtmlLint.IsRejectedGeneratedHtml(anchorDirty)); // D17
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
