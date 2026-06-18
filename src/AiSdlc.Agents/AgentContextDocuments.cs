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
    public const string RepairModeDocumentName = "Repair Mode (targeted fix)";
    public const string AuthPostureDocumentName = "Authentication Posture";

    // The no-auth shell variant (Charter "Needs auth: no" / NeedsAuth=false) ships NO Clerk, NO
    // src/api/Auth/, and NO tests/e2e/specs/auth.spec.ts. v010 showed the upstream spec + agents still
    // treated auth.spec as immutable-must-pass and demanded Clerk modal "affordances" for a no-auth
    // app → the implementer authored `@clerk/clerk-react` into a shell that doesn't install it →
    // TS2307 build break. Conditionalising only the implementer's Scaffold Contract (#145) wasn't
    // enough; this posture reaches EVERY agent (planning through implementation) and overrides any
    // Clerk/auth references the request or earlier documents carry. (Pairs with the Yorrixx-side fix
    // to stop the Definition-of-Done generation requiring Clerk for no-auth apps — see
    // docs/roadmap/conditional-auth-yorrixx-brief.md.)
    private const string NoAuthPostureInstructions = """
        This application has NO AUTHENTICATION. The charter specifies "Needs auth: no", and it is built
        on the no-auth shell variant, which ships NO Clerk, NO src/api/Auth/, and NO
        tests/e2e/specs/auth.spec.ts.

        THIS OVERRIDES ANY CONTRARY INSTRUCTION in the request, the charter, the Definition of Done, or
        any earlier document. If anything mentions Clerk, ClerkProvider, @clerk/clerk-react,
        VITE_CLERK_PUBLISHABLE_KEY, sign-in / sign-up / sign-out, a "signed-in" affordance, useUser(),
        or auth.spec.ts, treat it as OBSOLETE for this app: do NOT carry it into your plan, design,
        acceptance criteria, Definition of Done, tests, or code. There are no auth affordances to
        render, require, or test. The frontend has no Clerk dependency installed, so any Clerk import
        breaks the build. Plan, design, and verify this as a purely unauthenticated application.
        """;

    // Reaches every agent via AddStandard. Without it, a reopen-repair runs the full planning
    // pipeline (Strategist/BA/Architect) which re-plans the whole app from the charter and
    // overrides a narrow finding — v003: an AuthGate finding got reframed as "implement the
    // acceptance tests", which then dead-locked against the (already complete, protected) test
    // specs. This pins the whole pipeline to the findings.
    private const string RepairModeInstructions = """
        This run is a TARGETED REPAIR of an existing, already-deployed application — NOT a fresh
        build. Address ONLY the verification / CI findings provided in this context.

        - The app, its features, its pages, and its test files ALREADY EXIST and are complete.
          Do NOT re-plan or re-implement existing functionality, and do NOT treat existing code
          or tests as missing or as stubs to be written.
        - Scope every output strictly to fixing the stated findings against the existing code.
          If the findings name specific files, change only those and their direct dependencies.
        - Do NOT expand scope to "completing" the app, implementing acceptance tests, or adding
          features the findings did not ask for.
        """;

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

        // Only an explicit "false" selects the no-auth posture — mirrors the orchestrator, which sets
        // needsAuth from Charter.Constraints.NeedsAuth (a charter that omits the key deserialises to
        // false → no-auth). Absent metadata (no charter / off-path context) leaves auth apps untouched.
        if (string.Equals(ReadMeta(context, "needsAuth"), "false", StringComparison.OrdinalIgnoreCase))
            contextDocs[AuthPostureDocumentName] = NoAuthPostureInstructions;

        if (context.Mode == WorkflowMode.Bootstrap)
            contextDocs[OperatingModeDocumentName] = BootstrapOperatingModeInstructions;

        // Fresh CI findings take precedence: in a reopened run whose repair then fails CI,
        // the stale reopen findings describe already-fixed defects and must not compete
        // with the current compiler output.
        var ciFindings     = ReadMeta(context, "ciFindings");
        var reopenFindings = ReadMeta(context, "reopenFindings");
        if (!string.IsNullOrWhiteSpace(ciFindings))
        {
            contextDocs[CiFindingsDocumentName] = CiFindingsPreamble + ciFindings;
        }
        else if (!string.IsNullOrWhiteSpace(reopenFindings))
        {
            contextDocs[VerificationFindingsDocumentName] = VerificationFindingsPreamble + reopenFindings;
        }

        // A repair run (findings + existing source) is a surgical fix — pin the WHOLE pipeline to
        // the findings so the planning agents don't re-plan the app from the charter and bury a
        // narrow finding.
        var hasFindings = !string.IsNullOrWhiteSpace(ciFindings) || !string.IsNullOrWhiteSpace(reopenFindings);
        if (hasFindings && !string.IsNullOrWhiteSpace(ReadMeta(context, "existingSource")))
            contextDocs[RepairModeDocumentName] = RepairModeInstructions;
    }

    private static string ReadMeta(AgentContext context, string key) =>
        context.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
