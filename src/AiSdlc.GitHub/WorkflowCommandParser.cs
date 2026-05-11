namespace AiSdlc.GitHub;

public static class WorkflowCommandParser
{
    // Commands are matched against the first non-whitespace line of the comment body.
    // This lets reviewers add explanatory text after the command on subsequent lines.

    private static readonly (string Prefix, WorkflowCommand Command)[] KnownCommands =
    [
        ("/approve-brief",  WorkflowCommand.ApproveBrief),
        ("/request-changes", WorkflowCommand.RequestChanges),
        ("/approve-release", WorkflowCommand.ApproveRelease)
    ];

    public static WorkflowCommand Parse(string commentBody)
    {
        ArgumentNullException.ThrowIfNull(commentBody);

        var firstLine = FirstNonEmptyLine(commentBody);
        if (firstLine.Length == 0)
            return WorkflowCommand.None;

        foreach (var (prefix, command) in KnownCommands)
        {
            if (firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (firstLine.Length == prefix.Length || char.IsWhiteSpace(firstLine[prefix.Length])))
            {
                return command;
            }
        }

        return WorkflowCommand.None;
    }

    private static ReadOnlySpan<char> FirstNonEmptyLine(string text)
    {
        var span = text.AsSpan();
        while (!span.IsEmpty)
        {
            var newline = span.IndexOfAny('\r', '\n');
            var line = newline >= 0 ? span[..newline] : span;
            var trimmed = line.Trim();
            if (!trimmed.IsEmpty)
                return trimmed;
            span = newline >= 0 ? span[(newline + 1)..] : ReadOnlySpan<char>.Empty;
        }
        return ReadOnlySpan<char>.Empty;
    }
}
