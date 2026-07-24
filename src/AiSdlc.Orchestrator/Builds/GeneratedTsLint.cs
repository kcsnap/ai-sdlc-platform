using System.Text.RegularExpressions;

namespace AiSdlc.Orchestrator.Builds;

/// <summary>
/// D13 pre-commit lint for GENERATED .ts/.tsx: interpolating an imported FUNCTION identifier into a
/// template literal (<c>`${apiUrl}/api/bookings`</c> instead of <c>`${apiUrl('/api/bookings')}`</c>)
/// stringifies the function's source into the URL. The booking app shipped exactly that at three call
/// sites — the SPA fallback answered 200 for the garbage URL and every gate stayed green while the
/// frontend was completely disconnected from its API. TypeScript does not flag <c>${fn}</c>.
/// Pure + deterministic: only identifiers IMPORTED by the same file are candidates, so ordinary
/// string variables interpolate freely.
/// </summary>
public static partial class GeneratedTsLint
{
    // import { a, b as c } from '...' / import d from '...' — captures the braces list and default name.
    [GeneratedRegex(@"import\s+(?:(?<default>[A-Za-z_$][\w$]*)\s*,?\s*)?(?:\{(?<named>[^}]*)\})?\s*from\s*['""]", RegexOptions.None)]
    private static partial Regex ImportStatement();

    public sealed record Violation(string Identifier, string Excerpt);

    /// <summary>Scans one TS/TSX source; a violation per bare interpolation of an imported identifier.</summary>
    public static IReadOnlyList<Violation> Scan(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var imported = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match import in ImportStatement().Matches(source))
        {
            if (import.Groups["default"].Success)
                imported.Add(import.Groups["default"].Value);
            if (import.Groups["named"].Success)
                foreach (var part in import.Groups["named"].Value.Split(','))
                {
                    // "a" or "a as b" — the LOCAL binding is what appears in code.
                    var pieces = part.Split(" as ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var local = pieces.Length == 2 ? pieces[1] : pieces.ElementAtOrDefault(0)?.Trim();
                    if (!string.IsNullOrWhiteSpace(local)) imported.Add(local!);
                }
        }
        if (imported.Count == 0) return [];

        var violations = new List<Violation>();
        // `${ident}` with NOTHING else inside the braces — a call (`${ident(...)}`), member access, or
        // any expression is fine; the bare identifier is the stringify-the-function defect.
        foreach (Match m in Regex.Matches(source, @"\$\{\s*(?<id>[A-Za-z_$][\w$]*)\s*\}"))
        {
            var id = m.Groups["id"].Value;
            if (!imported.Contains(id)) continue;
            var start = Math.Max(0, m.Index - 30);
            violations.Add(new Violation(id, source.Substring(start, Math.Min(80, source.Length - start)).Trim()));
        }
        return violations;
    }

    /// <summary>True when a generated TS/TSX file trips the lint — the change must not commit.</summary>
    public static bool IsRejectedGeneratedTs(AiSdlc.Shared.FileChange change) =>
        (change.Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
         || change.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrEmpty(change.Content)
        && Scan(change.Content).Count > 0;
}
