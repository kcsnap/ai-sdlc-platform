using System.Text.RegularExpressions;

namespace AiSdlc.Agents;

internal static partial class BusinessAnalystSpecParser
{
    public static BusinessAnalystChangeRequest Parse(string? title, string specMarkdown, string? existingProductContext)
    {
        var normalized = specMarkdown ?? string.Empty;

        return new BusinessAnalystChangeRequest
        {
            Title = title,
            ChangeRequest = GetSection(normalized, "What do you want to create or change?"),
            BusinessNeed = GetSection(normalized, "Why is this needed?"),
            TargetUser = GetSection(normalized, "Who is the user or customer?"),
            AppType = GetCheckedOptions(GetSection(normalized, "Is this for a new app or an existing app?")),
            Constraints = GetSection(normalized, "Any known constraints?"),
            ReferenceMaterial = GetSection(normalized, "Any examples, screenshots, links, or reference material?"),
            DefinitionOfDone = GetSection(normalized, "Definition of done, if known"),
            ExistingProductContext = existingProductContext?.Trim() ?? string.Empty,
            RawSpecMarkdown = normalized
        };
    }

    private static string GetCheckedOptions(string sectionText)
    {
        var checkedItems = CheckedItemRegex()
            .Matches(sectionText)
            .Select(match => match.Groups["label"].Value.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();

        return checkedItems.Length == 0 ? sectionText.Trim() : string.Join(", ", checkedItems);
    }

    private static string GetSection(string markdown, string heading)
    {
        var pattern = $@"(?ms)^##\s+{Regex.Escape(heading)}\s*$\s*(?<content>.*?)(?=^##\s+|\z)";
        var match = Regex.Match(markdown, pattern, RegexOptions.Multiline);
        return match.Success ? match.Groups["content"].Value.Trim() : string.Empty;
    }

    [GeneratedRegex(@"^- \[x\]\s+(?<label>.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CheckedItemRegex();
}
