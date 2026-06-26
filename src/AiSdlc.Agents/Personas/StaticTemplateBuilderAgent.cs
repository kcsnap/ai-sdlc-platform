using System.Text;
using System.Text.Json;
using AiSdlc.Agents.Templates;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;
using AiSdlc.Shared.Templates;

namespace AiSdlc.Agents.Personas;

/// <summary>
/// Template-first Static builder: SELECTS a pre-built template and FILLS its slots with a cheap model,
/// then assembles deterministically — no LLM writes markup. Replaces the Opus Code Implementer on fresh
/// Static builds (orchestrator-gated, with fallback). See docs/roadmap/template-first-static.md.
/// </summary>
public sealed class StaticTemplateBuilderAgent : IAgent
{
    private const string SystemPrompt = """
        You build a STATIC marketing one-page website by SELECTING the best-fit pre-built template and
        FILLING ITS SLOTS — you do NOT write HTML or CSS. Choose one template from the catalogue, then
        produce real, on-brand values for every one of its tokens.

        Return STRICT JSON ONLY (no prose, no code fences) in exactly this shape:
        {
          "templateId": "<one id from the catalogue>",
          "brand":   { "<BRAND_TOKEN>": "<value>", ... every brand token for the chosen template },
          "content": { "<CONTENT_TOKEN>": "<value>", ... every content token for the chosen template },
          "repeat":  { "<block>": [ { "<TOKEN>": "<value>", ... }, ... ], ... every repeat block }
        }

        Rules:
        - Fill EVERY token the chosen template declares (brand + content + each repeat block). A missing
          token fails the build.
        - Repeat blocks: provide between the block's stated min and max items.
        - brand: real hex colours that evoke the domain; a CHARACTERFUL Google Font pairing (display + body)
          — never Inter/Roboto/Arial as the display face. FONT_DISPLAY / FONT_BODY are CSS font-family
          stacks (e.g. "\"Fraunces\", serif"). GOOGLE_FONTS_HREF is the matching fonts.googleapis.com
          stylesheet URL for BOTH families. BRAND_INITIAL is one uppercase letter; THEME_COLOR is a hex.
        - content: real, specific, on-brand copy — no lorem, no placeholder. CONTACT_SUBJECT must be
          URL-encoded (spaces -> %20). Keep headings tight and benefit-led.
        - Do NOT output any contact email — contact links are handled by the template and substituted at
          deploy. Pick the template whose archetype + moods best fit the brand. Output JSON only.
        """;

    private readonly IModelProvider _model;
    private readonly StaticTemplateLibrary _library;

    public StaticTemplateBuilderAgent(IModelProvider model, StaticTemplateLibrary library)
    {
        _model = model;
        _library = library;
    }

    public string Name => AgentNames.StaticTemplateBuilder;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var templates = _library.All;
        if (templates.Count == 0)
            throw new InvalidOperationException("Static template library is empty.");

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName    = Name,
            TaskType     = "StaticTemplateBuild",
            SystemPrompt = SystemPrompt,
            UserPrompt   = BuildUserPrompt(request.Context, templates),
            MaxTokens    = 4000
        }, cancellationToken);

        var (templateId, input) = ParseSlots(response.ResponseText);
        var template = _library.Get(templateId) ?? templates[0];

        // Assemble deterministically; throws on any unfilled token / bad repeat count → the orchestrator
        // falls back to the Code Implementer. The shipped acceptance.spec.ts travels as one of the files.
        var files = TemplateAssembler.Assemble(template, input);

        var sb = new StringBuilder();
        foreach (var file in files)
        {
            sb.Append("<file path=\"").Append(file.Path).Append("\">\n");
            sb.Append(file.Content);
            if (!file.Content.EndsWith('\n')) sb.Append('\n');
            sb.Append("</file>\n\n");
        }

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Built static site from template '{template.Manifest.Id}' ({files.Count} files).",
            OutputMarkdown   = sb.ToString(),
            Decision         = $"Template: {template.Manifest.Id}",
            ArtefactsCreated = files.Select(f => f.Path).ToList()
        };
    }

    private static string BuildUserPrompt(AgentContext ctx, IReadOnlyList<StaticTemplate> templates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Brand brief");
        sb.AppendLine($"Site: {GetMeta(ctx, "issueTitle")}");
        AppendIf(sb, GetMeta(ctx, "issueBody"), null);
        AppendIf(sb, GetMeta(ctx, "ownerBrief"), "### Product brief");
        AppendIf(sb, GetMeta(ctx, "uxOutput"), "### Design direction (use its palette / type / mood if present)");

        sb.AppendLine();
        sb.AppendLine("## Template catalogue — choose ONE templateId");
        foreach (var t in templates)
        {
            var m = t.Manifest;
            sb.AppendLine($"### {m.Id} — {m.Name}");
            sb.AppendLine($"archetype: {m.Archetype}");
            sb.AppendLine($"moods: {string.Join(", ", m.Moods)}; bestFor: {string.Join(", ", m.BestFor)}");
            sb.AppendLine($"brandTokens: {string.Join(", ", m.BrandTokens)}");
            sb.AppendLine($"contentTokens: {string.Join(", ", m.ContentTokens)}");
            foreach (var (block, rule) in m.Repeatables)
                sb.AppendLine($"repeat '{block}' ({rule.Min}-{rule.Max} items, tokens: {string.Join(", ", rule.Tokens)})");
        }
        return sb.ToString();
    }

    private static (string templateId, TemplateAssemblyInput input) ParseSlots(string raw)
    {
        using var doc = JsonDocument.Parse(ExtractJson(raw));
        var root = doc.RootElement;

        var templateId = root.TryGetProperty("templateId", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() ?? string.Empty
            : string.Empty;

        return (templateId, new TemplateAssemblyInput
        {
            Brand    = ReadStringMap(root, "brand"),
            Content  = ReadStringMap(root, "content"),
            Platform = new Dictionary<string, string> { ["YEAR"] = DateTime.UtcNow.Year.ToString() },
            Repeat   = ReadRepeat(root)
        });
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string property)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty(property, out var obj) && obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject())
                map[p.Name] = AsString(p.Value);
        return map;
    }

    private static Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadRepeat(JsonElement root)
    {
        var result = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
        if (root.TryGetProperty("repeat", out var obj) && obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var block in obj.EnumerateObject())
            {
                if (block.Value.ValueKind != JsonValueKind.Array) continue;
                var items = new List<IReadOnlyDictionary<string, string>>();
                foreach (var element in block.Value.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object) continue;
                    var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var p in element.EnumerateObject())
                        dict[p.Name] = AsString(p.Value);
                    items.Add(dict);
                }
                result[block.Name] = items;
            }
        }
        return result;
    }

    private static string AsString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();

    private static string ExtractJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : "{}";
    }

    private static void AppendIf(StringBuilder sb, string value, string? heading)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (heading is not null) sb.AppendLine(heading);
        sb.AppendLine(value);
    }

    private static string GetMeta(AgentContext ctx, string key) =>
        ctx.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
