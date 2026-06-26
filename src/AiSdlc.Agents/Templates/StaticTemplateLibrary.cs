using AiSdlc.Shared.Templates;

namespace AiSdlc.Agents.Templates;

/// <summary>
/// Loads the static template library embedded into this assembly (templates/static/** → resource names
/// "tpl/&lt;dir&gt;/&lt;file&gt;"). Cached after first use. Registered as a singleton.
/// </summary>
public sealed class StaticTemplateLibrary
{
    private const string Prefix = "tpl/";
    private readonly Lazy<IReadOnlyDictionary<string, StaticTemplate>> _byId;

    public StaticTemplateLibrary() => _byId = new Lazy<IReadOnlyDictionary<string, StaticTemplate>>(Load);

    public IReadOnlyList<StaticTemplate> All => _byId.Value.Values.ToList();

    public StaticTemplate? Get(string id) =>
        !string.IsNullOrEmpty(id) && _byId.Value.TryGetValue(id, out var t) ? t : null;

    private static IReadOnlyDictionary<string, StaticTemplate> Load()
    {
        var asm = typeof(StaticTemplateLibrary).Assembly;
        var byDir = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            // RecursiveDir uses '\' on Windows builds and '/' on Linux (CI) — normalise both.
            var rel = name[Prefix.Length..].Replace('\\', '/');
            var slash = rel.IndexOf('/');
            if (slash < 0) continue; // top-level file (e.g. README.md) is not part of a template

            var dir = rel[..slash];
            var file = rel[(slash + 1)..];

            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            if (!byDir.TryGetValue(dir, out var files))
                byDir[dir] = files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            files[file] = reader.ReadToEnd();
        }

        var result = new Dictionary<string, StaticTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var files in byDir.Values)
        {
            if (!files.TryGetValue("manifest.json", out var manifestJson)) continue;
            var manifest = TemplateManifest.Parse(manifestJson);
            result[manifest.Id] = new StaticTemplate(manifest, files);
        }
        return result;
    }
}
