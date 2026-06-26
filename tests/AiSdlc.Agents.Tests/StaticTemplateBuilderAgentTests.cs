using System.Text.Json;
using System.Text.RegularExpressions;
using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.Agents.Templates;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

public sealed class StaticTemplateBuilderAgentTests
{
    [Fact]
    public void Library_loads_both_embedded_templates()
    {
        var lib = new StaticTemplateLibrary();

        Assert.True(lib.All.Count >= 2);
        var classic = lib.Get("classic-centered");
        Assert.NotNull(classic);
        Assert.True(classic!.Files.ContainsKey("template.html"));
        Assert.True(classic.Files.ContainsKey("manifest.json"));
        Assert.NotNull(lib.Get("split-feature"));
    }

    [Fact]
    public async Task Builds_a_static_site_from_the_selected_template()
    {
        var lib = new StaticTemplateLibrary();
        var json = FullSlotJson(lib, "classic-centered");

        var agent = new StaticTemplateBuilderAgent(new JsonProvider(json), lib);
        var result = await agent.ExecuteAsync(new AgentExecutionRequest { AgentName = AgentNames.StaticTemplateBuilder, Context = MakeContext() }, CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Contains("<file path=\"index.html\">", result.OutputMarkdown);
        Assert.Contains("<file path=\"tests/e2e/specs/acceptance.spec.ts\">", result.OutputMarkdown);
        Assert.Contains("data-testid=\"hero-cta\"", result.OutputMarkdown);
        Assert.Contains("mailto:__CONTACT_EMAIL__", result.OutputMarkdown);   // deploy token preserved
        Assert.DoesNotMatch(@"\{\{[^}]+\}\}", result.OutputMarkdown!);        // every slot filled
    }

    [Fact]
    public async Task Unknown_template_id_falls_back_to_an_available_template()
    {
        var lib = new StaticTemplateLibrary();
        // Fill classic-centered's slots but claim a non-existent id → agent uses the first template.
        var json = FullSlotJson(lib, "classic-centered").Replace("\"classic-centered\"", "\"does-not-exist\"");

        var agent = new StaticTemplateBuilderAgent(new JsonProvider(json), lib);
        var result = await agent.ExecuteAsync(new AgentExecutionRequest { AgentName = AgentNames.StaticTemplateBuilder, Context = MakeContext() }, CancellationToken.None);

        // Fallback only assembles cleanly if the first template happens to be classic-centered; either way
        // it must not crash and must produce files. (Ordering is deterministic within a run.)
        Assert.Equal("Completed", result.Status);
        Assert.Contains("<file path=\"index.html\">", result.OutputMarkdown);
    }

    // Builds a JSON payload filling EVERY declared token of the given template (values are placeholders —
    // the assembler only substitutes, it does not validate content).
    private static string FullSlotJson(StaticTemplateLibrary lib, string templateId)
    {
        var m = lib.Get(templateId)!.Manifest;
        var brand = m.BrandTokens.ToDictionary(t => t, t => "x");
        var content = m.ContentTokens.ToDictionary(t => t, t => "x");
        var repeat = m.Repeatables.ToDictionary(
            r => r.Key,
            r => Enumerable.Range(0, r.Value.Min)
                .Select(_ => r.Value.Tokens.ToDictionary(t => t, t => "x"))
                .ToArray());

        return JsonSerializer.Serialize(new { templateId, brand, content, repeat });
    }

    private static AgentContext MakeContext() => new()
    {
        RunId = "r", Repository = "yorrixx-apps/user-app-abc", IssueNumber = 1,
        CurrentState = "x", RequestedAgent = AgentNames.StaticTemplateBuilder,
        Metadata = { ["issueTitle"] = "BrightSmile Dental", ["stackProfile"] = "Static" }
    };

    private sealed class JsonProvider(string json) : IModelProvider
    {
        public string ProviderName => "fake";
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelResponse
            {
                ProviderName = "fake", ModelName = "fake", ResponseText = json,
                Usage = new Dictionary<string, object>(), WasTruncated = false, Warnings = new List<string>()
            });
    }
}
