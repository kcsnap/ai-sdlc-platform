using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiSdlc.Shared.Templates;

/// <summary>
/// The machine-readable slot contract for a static template (parsed from its <c>manifest.json</c>).
/// Declares which tokens the content model fills, the repeatable blocks and their counts, and the
/// mapping from template source files to the output app paths. See <c>templates/static/README.md</c>.
/// </summary>
public sealed record TemplateManifest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Archetype { get; init; } = string.Empty;
    public IReadOnlyList<string> Moods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BestFor { get; init; } = Array.Empty<string>();

    /// <summary>output app path → template source file name (e.g. "index.html" → "template.html").</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> BrandTokens { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ContentTokens { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PlatformTokens { get; init; } = Array.Empty<string>();

    /// <summary>block name → its repeat rule (token set + min/max item count).</summary>
    public IReadOnlyDictionary<string, Repeatable> Repeatables { get; init; } = new Dictionary<string, Repeatable>();

    public IReadOnlyList<string> DeployTokens { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DataTestIds { get; init; } = Array.Empty<string>();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static TemplateManifest Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<TemplateManifest>(json, Options)
            ?? throw new TemplateAssemblyException("manifest.json deserialized to null.");
    }
}

/// <summary>A repeatable block (card/nav list): the tokens each item supplies and the allowed item count.</summary>
public sealed record Repeatable
{
    public int Min { get; init; }
    public int Max { get; init; }
    public IReadOnlyList<string> Tokens { get; init; } = Array.Empty<string>();
}
