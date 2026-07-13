using System.Text;
using System.Text.RegularExpressions;

namespace AiSdlc.Shared.Templates;

/// <summary>
/// Deterministic, LLM-free assembly of a static template into committable files. Expands
/// <c>&lt;!-- REPEAT:x --&gt;</c> blocks, substitutes <c>{{TOKEN}}</c> slots, and FAILS if any token is left
/// unresolved or a repeat count is out of range. Deploy tokens (e.g. <c>__CONTACT_EMAIL__</c>) are not
/// <c>{{ }}</c>-shaped, so they pass through untouched for deploy-time substitution. This is the core of the
/// template-first Static path — see <c>docs/roadmap/template-first-static.md</c>.
/// </summary>
public static partial class TemplateAssembler
{
    [GeneratedRegex(@"<!--\s*REPEAT:([A-Za-z0-9_]+)\s*-->(.*?)<!--\s*/REPEAT:\1\s*-->", RegexOptions.Singleline)]
    private static partial Regex RepeatBlock();

    [GeneratedRegex(@"\{\{([A-Z0-9_]+)\}\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\{\{[^}]+\}\}")]
    private static partial Regex AnyToken();

    public static IReadOnlyList<FileChange> Assemble(StaticTemplate template, TemplateAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(input);

        var problems = new List<string>();

        // 1. Repeat-count validation against the manifest's declared min/max.
        foreach (var (name, rule) in template.Manifest.Repeatables)
        {
            var count = input.Repeat.TryGetValue(name, out var items) ? items.Count : 0;
            if (count < rule.Min || count > rule.Max)
                problems.Add($"repeatable '{name}': {count} item(s), expected {rule.Min}-{rule.Max}.");
        }

        // 2. Merge scalar tokens (brand + content + platform; later source wins on conflict).
        var scalars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in new[] { input.Brand, input.Content, input.Platform })
            foreach (var (key, value) in source)
                scalars[key] = value;

        // 3. Assemble each output file: expand repeats → substitute scalars → check for leftovers.
        var outputs = new List<FileChange>();
        foreach (var (outputPath, sourceName) in template.Manifest.Files)
        {
            if (!template.Files.TryGetValue(sourceName, out var content))
            {
                problems.Add($"file '{outputPath}': source '{sourceName}' not found in template.");
                continue;
            }

            // Model-authored copy is PLAIN TEXT by contract; in HTML-context files it must be entity-
            // encoded at fill time (w1proof3: "Bouquets & Ludlow" hit html-validate's no-raw-characters
            // and failed the build). Decode-then-encode so copy the model pre-encoded ("&amp;") doesn't
            // double-encode. Non-HTML files (css/js/spec) take values verbatim.
            var htmlContext = HtmlExtensions.Contains(Path.GetExtension(outputPath));
            content = ExpandRepeats(content, input.Repeat, htmlContext);
            content = Substitute(content, scalars, htmlContext);

            foreach (var unresolved in AnyToken().Matches(content).Select(m => m.Value).Distinct())
                problems.Add($"file '{outputPath}': unresolved token {unresolved}.");

            outputs.Add(new FileChange(outputPath, content));
        }

        if (problems.Count > 0)
            throw new TemplateAssemblyException(template.Manifest.Id, problems);

        return outputs;
    }

    private static readonly HashSet<string> HtmlExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".html", ".htm", ".svg", ".xml" };

    private static string ExpandRepeats(
        string content,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> repeat,
        bool htmlEncode) =>
        RepeatBlock().Replace(content, match =>
        {
            var name = match.Groups[1].Value;
            var inner = match.Groups[2].Value;
            if (!repeat.TryGetValue(name, out var items) || items.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var item in items)
                sb.Append(Substitute(inner, item, htmlEncode));
            return sb.ToString();
        });

    private static string Substitute(string content, IReadOnlyDictionary<string, string> values, bool htmlEncode) =>
        Token().Replace(content, match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? (htmlEncode ? EncodeForHtml(value) : value)
                : match.Value);

    // Decode-then-encode: idempotent for values the model already entity-encoded, and turns raw
    // & < > " into valid entities everywhere else.
    private static string EncodeForHtml(string value) =>
        System.Net.WebUtility.HtmlEncode(System.Net.WebUtility.HtmlDecode(value));
}
