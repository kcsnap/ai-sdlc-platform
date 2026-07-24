using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace AiSdlc.Orchestrator.Builds;

/// <summary>
/// D8 pre-commit lint for GENERATED .html: after a real DOM parse, entity-escaped markup that was
/// meant to render becomes literal tag text inside TEXT NODES ("&lt;svg width=…" → text "<svg width=…").
/// fresh-w2-florist committed three feature icons that way and every substring gate stayed green —
/// the browser showed tag soup. Catching it before commit saves a full verify→repair round.
/// Pure + deterministic (AngleSharp parse, no rendering).
/// </summary>
public static partial class GeneratedHtmlLint
{
    // A tag-shaped sequence appearing as TEXT: "<svg ", "</div>", "<path/>" … Also common SVG
    // attribute fragments that betray a half-escaped blob even without a full opener.
    [GeneratedRegex(@"</?[A-Za-z][A-Za-z0-9-]*[\s>/]|stroke-width=|viewBox=", RegexOptions.None)]
    private static partial Regex MarkupInText();

    // Elements whose text content is LEGITIMATELY allowed to contain markup-shaped strings.
    private static readonly HashSet<string> CodeLikeElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "pre", "code", "textarea", "noscript", "title"
    };

    public sealed record Violation(string Excerpt);

    /// <summary>Scans one HTML document's text nodes; returns an excerpt per offending node.</summary>
    public static IReadOnlyList<Violation> Scan(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var violations = new List<Violation>();
        var document = new HtmlParser().ParseDocument(html);

        foreach (var node in Walk(document.DocumentElement))
        {
            if (node.NodeType != NodeType.Text) continue;
            if (HasCodeLikeAncestor(node)) continue;
            var text = node.TextContent;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var m = MarkupInText().Match(text);
            if (m.Success)
            {
                var start = Math.Max(0, m.Index - 20);
                violations.Add(new Violation(text.Substring(start, Math.Min(80, text.Length - start)).Trim()));
            }
        }

        // D17: dead in-page anchors — HealthyChicken shipped nav href="#hero" while the section carried
        // only data-testid="hero" (substring greps hid it; getElementById never lies). A real DOM parse
        // makes the check exact: every same-page fragment link must resolve to an element id. Governs the
        // FALLBACK Code Implementer path too, which TokenRules never see.
        var ids = document.All.Where(e => !string.IsNullOrEmpty(e.Id)).Select(e => e.Id!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var a in document.QuerySelectorAll("a[href^='#']"))
        {
            var href = a.GetAttribute("href")!;
            if (href.Length > 1 && !ids.Contains(href[1..]))
                violations.Add(new Violation($"dead in-page anchor: {href} has no matching id"));
        }
        return violations;
    }

    /// <summary>True when a generated file is HTML and trips the lint — the change must not commit.</summary>
    public static bool IsRejectedGeneratedHtml(AiSdlc.Shared.FileChange change) =>
        (change.Path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
         || change.Path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrEmpty(change.Content)
        && Scan(change.Content).Count > 0;

    private static IEnumerable<INode> Walk(INode? root)
    {
        if (root is null) yield break;
        var stack = new Stack<INode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.ChildNodes)
                stack.Push(child);
        }
    }

    private static bool HasCodeLikeAncestor(INode node)
    {
        for (var p = node.Parent; p is not null; p = p.Parent)
            if (p is IElement el && CodeLikeElements.Contains(el.LocalName))
                return true;
        return false;
    }
}
