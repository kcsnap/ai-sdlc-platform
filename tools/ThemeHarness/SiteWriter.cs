using System.Text;
using System.Text.RegularExpressions;

namespace ThemeHarness;

/// <summary>Parses the model's "===FILE: name===" delimited output into files and writes them out.</summary>
public static partial class SiteWriter
{
    [GeneratedRegex(@"^===FILE:\s*(?<path>.+?)\s*===\s*$")]
    private static partial Regex FileMarker();

    [GeneratedRegex(@"^===END===\s*$")]
    private static partial Regex EndMarker();

    public sealed record SiteFile(string Path, string Content);

    public static IReadOnlyList<SiteFile> Parse(string raw)
    {
        // Models sometimes wrap the whole thing in a markdown fence — strip one outer fence if present.
        var text = raw.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0) text = text[(firstNewline + 1)..];
            if (text.EndsWith("```")) text = text[..^3];
        }

        var files = new List<SiteFile>();
        string? currentPath = null;
        var content = new StringBuilder();

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            var marker = FileMarker().Match(trimmed);
            if (marker.Success)
            {
                Flush(files, currentPath, content);
                currentPath = marker.Groups["path"].Value.Trim();
                content.Clear();
                continue;
            }

            if (EndMarker().IsMatch(trimmed))
            {
                Flush(files, currentPath, content);
                currentPath = null;
                content.Clear();
                continue;
            }

            if (currentPath is not null)
                content.Append(trimmed).Append('\n');
        }

        Flush(files, currentPath, content);
        return files;
    }

    private static void Flush(List<SiteFile> files, string? path, StringBuilder content)
    {
        if (path is null) return;
        files.Add(new SiteFile(path, content.ToString().TrimEnd('\n') + "\n"));
    }

    public static void Write(string outputDir, IReadOnlyList<SiteFile> files)
    {
        Directory.CreateDirectory(outputDir);
        foreach (var file in files)
        {
            // Guard against path escapes from a misbehaving model.
            var safeRelative = file.Path.Replace('\\', '/').TrimStart('/');
            if (safeRelative.Contains(".."))
                throw new InvalidOperationException($"Refusing to write suspicious path: {file.Path}");

            var fullPath = Path.Combine(outputDir, safeRelative);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, file.Content);
        }
    }
}
