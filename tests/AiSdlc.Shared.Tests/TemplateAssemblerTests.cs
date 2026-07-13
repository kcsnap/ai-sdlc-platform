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
}
