using AiSdlc.Shared;

namespace AiSdlc.Agents;

// Standard context documents that should reach every agent's model prompt.
// Single place to add new repo-wide context (e.g. spec deltas later) without
// touching every persona file again.
public static class AgentContextDocuments
{
    public const string CharterDocumentName = "App Charter";
    public const string OperatingModeDocumentName = "Operating Mode";

    private const string BootstrapOperatingModeInstructions = """
        This is a BOOTSTRAP run: a greenfield user-app being built unattended from a charter.
        There is no human reviewer who can answer follow-up questions.

        - Do NOT include an "## Open Questions" section in your output.
        - When the charter is silent or ambiguous on a decision, choose the safest sensible
          default and proceed. Prefer well-established conventions and minimal scope.
        - Briefly document any non-obvious assumptions you made (one line each, in an
          "## Assumptions" section if useful) so they are auditable — but never block on them.
        """;

    public static void AddStandard(Dictionary<string, string> contextDocs, AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(contextDocs);
        ArgumentNullException.ThrowIfNull(context);

        var charter = ReadMeta(context, "charter");
        if (!string.IsNullOrWhiteSpace(charter))
            contextDocs[CharterDocumentName] = charter;

        if (context.Mode == WorkflowMode.Bootstrap)
            contextDocs[OperatingModeDocumentName] = BootstrapOperatingModeInstructions;
    }

    private static string ReadMeta(AgentContext context, string key) =>
        context.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
