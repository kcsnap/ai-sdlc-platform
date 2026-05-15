using System.Text.RegularExpressions;

namespace AiSdlc.Shared;

public sealed record FileChange(string Path, string Content);

public static class CodeChangeParser
{
    // Matches ```path:filename\n...content...``` blocks in agent output
    private static readonly Regex FileBlockRegex = new(
        @"```path:([^\n`]+)\n([\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyList<FileChange> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        return FileBlockRegex.Matches(markdown)
            .Select(m => new FileChange(m.Groups[1].Value.Trim(), m.Groups[2].Value))
            .ToArray();
    }
}
