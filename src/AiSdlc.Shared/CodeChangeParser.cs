using System.Text.RegularExpressions;

namespace AiSdlc.Shared;

public sealed record FileChange(string Path, string Content);

public static class CodeChangeParser
{
    // Matches <file path="...">...</file> blocks — XML sentinels can't appear naturally in source files,
    // so this format is unambiguous even when file content contains nested fenced code blocks.
    private static readonly Regex FileBlockRegex = new(
        @"<file path=""([^""]+)"">\r?\n([\s\S]*?)\r?\n</file>",
        RegexOptions.Compiled);

    public static IReadOnlyList<FileChange> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        return FileBlockRegex.Matches(markdown)
            .Select(m => new FileChange(m.Groups[1].Value.Trim(), m.Groups[2].Value))
            .ToArray();
    }
}
