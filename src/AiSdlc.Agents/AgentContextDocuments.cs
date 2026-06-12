using AiSdlc.Shared;

namespace AiSdlc.Agents;

// Standard context documents that should reach every agent's model prompt.
// Single place to add new repo-wide context (e.g. spec deltas later) without
// touching every persona file again.
public static class AgentContextDocuments
{
    public const string CharterDocumentName = "App Charter";
    public const string OperatingModeDocumentName = "Operating Mode";
    public const string VerificationFindingsDocumentName = "Verification Findings";
    public const string CiFindingsDocumentName = "CI Failure Findings";

    private const string VerificationFindingsPreamble =
        "This issue was REOPENED because the previously released build failed downstream " +
        "verification. The findings below come from that verification. Your output MUST " +
        "address them — they take priority over regenerating from scratch.\n\n";

    private const string CiFindingsPreamble =
        "The pull request's CI build FAILED on the current branch code. The compiler/check " +
        "output below identifies the defects. Fix exactly these — this is an in-run repair " +
        "of code this pipeline just generated, not a regeneration.\n\n";

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

        // Fresh CI findings take precedence: in a reopened run whose repair then fails CI,
        // the stale reopen findings describe already-fixed defects and must not compete
        // with the current compiler output.
        var ciFindings = ReadMeta(context, "ciFindings");
        if (!string.IsNullOrWhiteSpace(ciFindings))
        {
            contextDocs[CiFindingsDocumentName] = CiFindingsPreamble + ciFindings;
        }
        else
        {
            var findings = ReadMeta(context, "reopenFindings");
            if (!string.IsNullOrWhiteSpace(findings))
                contextDocs[VerificationFindingsDocumentName] = VerificationFindingsPreamble + findings;
        }
    }

    private static string ReadMeta(AgentContext context, string key) =>
        context.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
