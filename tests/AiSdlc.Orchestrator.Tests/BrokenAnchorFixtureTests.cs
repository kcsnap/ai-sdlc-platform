using System.Text.RegularExpressions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

/// D11 evidence pins: the three W5B live pages (captured 2026-07-18 before teardown) each ship nav
/// anchors pointing at sections that do not exist — the defect the assembler token rules, the
/// acceptance-spec anchor test, and the render smoke now prevent. Green pages carry none.
public sealed class BrokenAnchorFixtureTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    internal static IReadOnlyList<string> BrokenAnchors(string html)
    {
        var ids = Regex.Matches(html, @"\bid=""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return Regex.Matches(html, @"href=""#([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(a => !ids.Contains(a))
            .Distinct()
            .ToList();
    }

    [Theory]
    [InlineData("w5b-piano-broken-anchors.html", new[] { "adult-beginners", "all-ages", "exam-prep" })]
    [InlineData("w5b-vets-broken-anchors.html",  new[] { "services" })]
    [InlineData("w5b-sail-broken-anchors.html",  new[] { "courses", "taster" })]
    public void W5b_fixtures_carry_the_exact_broken_anchors(string fixture, string[] expected)
        => Assert.Equal(expected.OrderBy(x => x), BrokenAnchors(Fixture(fixture)).OrderBy(x => x));

    // D11 finding: even the REVIEW-PASSED "green" pages carry dead anchors — the defect was platform-wide
    // and simply never checked. Pinned as-is: these fixtures predate the anchor gates.
    [Theory]
    [InlineData("my-crochets-live.html", new[] { "about", "gallery" })]
    [InlineData("ramp-w1-florist.html",  new[] { "gallery" })]
    public void Pre_gate_green_pages_also_carry_dead_anchors(string fixture, string[] expected)
        => Assert.Equal(expected.OrderBy(x => x), BrokenAnchors(Fixture(fixture)).OrderBy(x => x));
    // D17: the HealthyChicken shape — href="#hero" present, hero section carries ONLY data-testid="hero".
    // Substring matching would "resolve" it; the DOM parse must not.
    [Fact]
    public void Dead_anchor_with_only_data_testid_is_flagged()
    {
        const string html = """
            <nav><a href="#hero">Recipes</a><a href="#features">Why</a></nav>
            <main id="main"><section class="hero" data-testid="hero">h</section>
            <section id="features">f</section></main>
            """;

        var violations = AiSdlc.Orchestrator.Builds.GeneratedHtmlLint.Scan(html);

        var v = Assert.Single(violations);
        Assert.Contains("#hero", v.Excerpt);
    }

    [Fact]
    public void Resolving_anchors_and_external_links_are_clean()
    {
        const string html = """
            <nav><a href="#hero">Top</a><a href="https://x.example/#frag">Out</a><a href="#">Noop</a></nav>
            <section id="hero">h</section>
            """;

        Assert.Empty(AiSdlc.Orchestrator.Builds.GeneratedHtmlLint.Scan(html));
    }
}
