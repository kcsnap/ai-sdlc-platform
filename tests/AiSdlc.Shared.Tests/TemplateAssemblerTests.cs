using AiSdlc.Shared;
using AiSdlc.Shared.Templates;
using Xunit;

namespace AiSdlc.Shared.Tests;

public sealed class TemplateAssemblerTests
{
    // A minimal but representative template: scalar tokens, a REPEAT block, a deploy token, and the
    // output-path mapping — exercises every assembler behaviour without touching disk.
    private static StaticTemplate SampleTemplate(int featureMin = 2, int featureMax = 4) => new(
        new TemplateManifest
        {
            Id = "sample",
            Files = new Dictionary<string, string> { ["index.html"] = "page", ["styles.css"] = "css" },
            BrandTokens = new[] { "PRIMARY" },
            ContentTokens = new[] { "HERO" },
            PlatformTokens = new[] { "YEAR" },
            Repeatables = new Dictionary<string, Repeatable>
            {
                ["feature"] = new() { Min = featureMin, Max = featureMax, Tokens = new[] { "TITLE" } }
            }
        },
        new Dictionary<string, string>
        {
            ["page"] =
                "<main data-testid=\"app-ready\"><h1>{{HERO}}</h1>" +
                "<a data-testid=\"hero-cta\" href=\"mailto:__CONTACT_EMAIL__\">Contact</a>" +
                "<ul><!-- REPEAT:feature --><li>{{TITLE}}</li><!-- /REPEAT:feature --></ul>" +
                "<footer>{{YEAR}}</footer></main>",
            ["css"] = ":root{--p:{{PRIMARY}}}"
        });

    private static TemplateAssemblyInput SampleInput() => new()
    {
        Brand = new Dictionary<string, string> { ["PRIMARY"] = "#123456" },
        Content = new Dictionary<string, string> { ["HERO"] = "Welcome" },
        Platform = new Dictionary<string, string> { ["YEAR"] = "2026" },
        Repeat = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["feature"] = new IReadOnlyDictionary<string, string>[]
            {
                new Dictionary<string, string> { ["TITLE"] = "One" },
                new Dictionary<string, string> { ["TITLE"] = "Two" }
            }
        }
    };

    [Fact]
    public void Substitutes_scalars_and_expands_repeats()
    {
        var files = TemplateAssembler.Assemble(SampleTemplate(), SampleInput());

        var page = Assert.Single(files, f => f.Path == "index.html");
        Assert.Contains("<h1>Welcome</h1>", page.Content);
        Assert.Contains("<li>One</li><li>Two</li>", page.Content);
        Assert.Contains("<footer>2026</footer>", page.Content);
        var css = Assert.Single(files, f => f.Path == "styles.css");
        Assert.Contains("--p:#123456", css.Content);
    }

    [Fact]
    public void Maps_output_paths_from_the_manifest()
    {
        var files = TemplateAssembler.Assemble(SampleTemplate(), SampleInput());
        Assert.Equal(new[] { "index.html", "styles.css" }, files.Select(f => f.Path).OrderBy(p => p));
    }

    // w1proof3: "Bouquets & Ludlow" filled into index.html raw → html-validate no-raw-characters →
    // build failed. Values are entity-encoded in HTML-context files (decode-then-encode, so copy the
    // model pre-encoded is NOT double-encoded); non-HTML files take values verbatim.
    [Fact]
    public void Html_encodes_filled_values_in_html_files_only()
    {
        var input = new TemplateAssemblyInput
        {
            Brand = new Dictionary<string, string> { ["PRIMARY"] = "#123456" }, // css: must stay verbatim
            Content = new Dictionary<string, string> { ["HERO"] = "Bouquets & <Ludlow> Market" },
            Platform = new Dictionary<string, string> { ["YEAR"] = "2026" },
            Repeat = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["feature"] = new IReadOnlyDictionary<string, string>[]
                {
                    new Dictionary<string, string> { ["TITLE"] = "Fish & Chips" },
                    new Dictionary<string, string> { ["TITLE"] = "Already &amp; Encoded" }
                }
            }
        };

        var files = TemplateAssembler.Assemble(SampleTemplate(), input);

        var page = Assert.Single(files, f => f.Path == "index.html");
        Assert.Contains("<h1>Bouquets &amp; &lt;Ludlow&gt; Market</h1>", page.Content);
        Assert.Contains("<li>Fish &amp; Chips</li>", page.Content);
        Assert.Contains("<li>Already &amp; Encoded</li>", page.Content); // no &amp;amp;
        var css = Assert.Single(files, f => f.Path == "styles.css");
        Assert.Contains("--p:#123456", css.Content);
    }

    [Fact]
    public void Leaves_deploy_token_untouched_for_deploy_substitution()
    {
        var files = TemplateAssembler.Assemble(SampleTemplate(), SampleInput());
        var page = files.Single(f => f.Path == "index.html");
        Assert.Contains("mailto:__CONTACT_EMAIL__", page.Content);
        Assert.DoesNotMatch(@"\{\{.*?\}\}", page.Content); // no template tokens survive
    }

    [Fact]
    public void Throws_listing_unresolved_tokens_when_a_scalar_is_missing()
    {
        var input = SampleInput() with { Content = new Dictionary<string, string>() }; // HERO missing
        var ex = Assert.Throws<TemplateAssemblyException>(() => TemplateAssembler.Assemble(SampleTemplate(), input));
        Assert.Contains(ex.Problems, p => p.Contains("{{HERO}}") && p.Contains("index.html"));
    }

    [Fact]
    public void Throws_when_a_repeat_count_is_below_min()
    {
        var input = SampleInput() with
        {
            Repeat = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["feature"] = new IReadOnlyDictionary<string, string>[]
                {
                    new Dictionary<string, string> { ["TITLE"] = "Only one" }
                }
            }
        };
        var ex = Assert.Throws<TemplateAssemblyException>(() => TemplateAssembler.Assemble(SampleTemplate(featureMin: 2), input));
        Assert.Contains(ex.Problems, p => p.Contains("feature") && p.Contains("expected 2-4"));
    }

    [Fact]
    public void Missing_repeat_item_token_is_caught_as_unresolved()
    {
        var input = SampleInput() with
        {
            Repeat = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["feature"] = new IReadOnlyDictionary<string, string>[]
                {
                    new Dictionary<string, string> { ["WRONG"] = "x" },
                    new Dictionary<string, string> { ["TITLE"] = "ok" }
                }
            }
        };
        var ex = Assert.Throws<TemplateAssemblyException>(() => TemplateAssembler.Assemble(SampleTemplate(), input));
        Assert.Contains(ex.Problems, p => p.Contains("{{TITLE}}"));
    }

    [Fact]
    public void Parses_a_manifest_with_files_and_repeatables()
    {
        var json = """
        {
          "id": "classic-centered",
          "files": { "index.html": "template.html", "tests/e2e/specs/acceptance.spec.ts": "acceptance.spec.ts" },
          "brandTokens": ["BRAND_PRIMARY"],
          "repeatables": { "feature": { "min": 3, "max": 6, "tokens": ["ICON", "TITLE", "BODY"] } },
          "deployTokens": ["__CONTACT_EMAIL__"]
        }
        """;

        var m = TemplateManifest.Parse(json);

        Assert.Equal("classic-centered", m.Id);
        Assert.Equal("template.html", m.Files["index.html"]);
        Assert.Equal(3, m.Repeatables["feature"].Min);
        Assert.Equal(6, m.Repeatables["feature"].Max);
        Assert.Contains("TITLE", m.Repeatables["feature"].Tokens);
        Assert.Contains("__CONTACT_EMAIL__", m.DeployTokens);
    }

    // D8 root cause: the model returned a whole <svg> as the ICON glyph value; #237's encoder then
    // (correctly) escaped it into visible "&lt;svg …" text soup. Values are PLAIN TEXT by contract —
    // markup-shaped values are stripped at fill time; an all-markup value collapses to the fallback glyph.
    [Fact]
    public void Markup_shaped_fill_values_are_stripped_not_escaped_into_soup()
    {
        var input = new TemplateAssemblyInput
        {
            Brand = new Dictionary<string, string> { ["PRIMARY"] = "#123456" },
            Content = new Dictionary<string, string> { ["HERO"] = "Real <em>copy</em> here" },
            Platform = new Dictionary<string, string> { ["YEAR"] = "2026" },
            Repeat = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["feature"] = new IReadOnlyDictionary<string, string>[]
                {
                    new Dictionary<string, string> { ["TITLE"] = "<svg width=\"48\" height=\"48\" viewBox=\"0 0 48 48\"><path d=\"M1 2\"/></svg>" },
                    new Dictionary<string, string> { ["TITLE"] = "Plain title" }
                }
            }
        };

        var files = TemplateAssembler.Assemble(SampleTemplate(), input);
        var page = Assert.Single(files, f => f.Path == "index.html");

        Assert.DoesNotContain("&lt;svg", page.Content);                      // the D8 soup can't happen
        Assert.Contains("<li>◆</li>", page.Content);                    // all-markup value → fallback glyph
        Assert.Contains("<h1>Real copy here</h1>", page.Content);            // text survives tag-stripping
        Assert.Contains("<li>Plain title</li>", page.Content);
    }

    // ── D11: structural-token validation — W5B shipped nav anchors (#exam-prep, #courses, #services,
    // #taster, #all-ages, #adult-beginners) pointing at sections that don't exist, because HREF was
    // free-form and nothing validated it against the template's fixed structure.
    private static StaticTemplate NavTemplate() => new(
        TemplateManifest.Parse("""
        {
          "id": "nav-sample",
          "files": { "index.html": "page" },
          "contentTokens": ["HERO"],
          "repeatables": { "nav": { "min": 1, "max": 5, "tokens": ["LABEL", "HREF"] } },
          "tokenRules": {
            "HREF": { "oneOf": ["#main", "#features", "#contact"] },
            "THEME_COLOR": { "pattern": "^#[0-9a-fA-F]{6}$" }
          }
        }
        """),
        new Dictionary<string, string>
        {
            ["page"] = "<h1>{{HERO}}</h1><nav><!-- REPEAT:nav --><a href=\"{{HREF}}\">{{LABEL}}</a><!-- /REPEAT:nav --></nav>"
        });

    private static TemplateAssemblyInput NavInput(string href) => new()
    {
        Brand = new Dictionary<string, string>(),
        Content = new Dictionary<string, string> { ["HERO"] = "Hi" },
        Platform = new Dictionary<string, string>(),
        Repeat = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["nav"] = new IReadOnlyDictionary<string, string>[]
            {
                new Dictionary<string, string> { ["LABEL"] = "Lessons", ["HREF"] = href }
            }
        }
    };

    [Fact]
    public void Invented_nav_anchor_fails_assembly()
    {
        var ex = Assert.Throws<TemplateAssemblyException>(
            () => TemplateAssembler.Assemble(NavTemplate(), NavInput("#exam-prep"))); // piano's invented target

        Assert.Contains(ex.Problems, p => p.Contains("{{HREF}}") && p.Contains("#exam-prep") && p.Contains("#features"));
    }

    [Fact]
    public void Legal_nav_anchor_assembles()
    {
        var files = TemplateAssembler.Assemble(NavTemplate(), NavInput("#features"));
        Assert.Contains("href=\"#features\"", Assert.Single(files).Content);
    }

    [Fact]
    public void Pattern_rules_validate_scalar_tokens()
    {
        var input = NavInput("#main") with
        {
            Brand = new Dictionary<string, string> { ["THEME_COLOR"] = "tomato" } // not hex6
        };

        var ex = Assert.Throws<TemplateAssemblyException>(() => TemplateAssembler.Assemble(NavTemplate(), input));
        Assert.Contains(ex.Problems, p => p.Contains("{{THEME_COLOR}}") && p.Contains("tomato"));
    }
}
