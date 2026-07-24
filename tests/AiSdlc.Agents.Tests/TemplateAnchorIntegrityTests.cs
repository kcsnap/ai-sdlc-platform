using System.Text.RegularExpressions;
using AiSdlc.Agents.Templates;
using Xunit;

namespace AiSdlc.Agents.Tests;

/// <summary>
/// D17 (TDD red-first): HealthyChicken deployed with nav href="#hero" dead — the manifest blesses
/// "#hero" as a legal HREF while template.html's hero section carries only data-testid="hero", no
/// id="hero". (classic-centered also blesses "#about" with no about section at all.) Substring greps
/// hid it — data-testid="hero" CONTAINS id="hero" — so this test matches ids with a word boundary,
/// exactly like the browser's getElementById does.
/// </summary>
public sealed class TemplateAnchorIntegrityTests
{
    [Fact]
    public void Every_legal_nav_href_resolves_to_a_real_id_in_its_template()
    {
        var lib = new StaticTemplateLibrary();
        var failures = new List<string>();

        foreach (var template in lib.All)
        {
            var id = template.Manifest.Id;
            if (!template.Manifest.TokenRules.TryGetValue("HREF", out var rule) || rule.OneOf is null)
                continue;

            var html = template.Files["template.html"];
            var realIds = Regex.Matches(html, @"(?<![-\w])id=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var href in rule.OneOf.Where(h => h.StartsWith('#')))
                if (!realIds.Contains(href[1..]))
                    failures.Add($"{id}: legal HREF '{href}' has no id=\"{href[1..]}\" in template.html");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
