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
    public const string StackProfilePostureDocumentName = "Stack Profile Posture";
    public const string CapabilityPostureDocumentName = "Capability Posture";
    public const string CapabilityGapsDocumentName = "Capability Gaps (confirm at review)";
    public const string FormCapturePostureDocumentName = "Form Capture";

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
        breaks the build. On the BACKEND there is no authorization layer: do NOT add [Authorize] or
        [AllowAnonymous] attributes and do NOT reference Microsoft.AspNetCore.Authorization — that
        package is not installed, so those attributes will not compile. Every API function is already a
        plain, unauthenticated endpoint; there is nothing to mark "public" or "anonymous". Plan, design,
        and verify this as a purely unauthenticated application.
        """;

    // Static stack profile (Yorrixx stamps `.yorrixx/profile.json {stackProfile}`; the orchestrator
    // carries it in metadata — derive-once-stamp, the platform never re-derives). A Static app is
    // hand-written HTML/CSS(+JS) with NO React/Vite, NO C# API/Functions, NO Cosmos/DB. v011 built a
    // 1-page marketing site as full-stack and chased un-seeded Cosmos data that should have been
    // hard-coded. This posture reaches EVERY agent so none specs a backend for a static app. See
    // docs/roadmap/stack-profiles-static-first.md.
    private const string StaticProfilePosture = """
        This app is a STATIC site (Stack profile: Static). The entire app is hand-written HTML, CSS, and
        — only where it genuinely improves UX — a little vanilla JavaScript. There is NO React/Vite, NO
        C# Web API / Azure Functions, NO Cosmos or any database, and NO authentication.

        Author the page directly: index.html + styles.css (+ optional app.js). Hard-code any fixed data
        (e.g. a list of items) straight into the page; do NOT call a backend, fetch `/api/*`, or persist
        anything. Any "search" or filter is client-side over the hard-coded data.

        THIS OVERRIDES ANY CONTRARY INSTRUCTION in the request, the charter, the Definition of Done, or
        any earlier document. If anything assumes React, an npm/Vite build, a C# API, Azure Functions,
        Cosmos / a database, `fetch`/HTTP calls, or authentication, treat it as OBSOLETE for this app —
        do NOT add any of them; there is no backend or build step to host them. Plan, design, and verify
        this as a purely static site.
        """;

    // Api-only capability profile (the persistence axis resolved to NO database). The Architect derives
    // whether the app needs persistence (Balanced); a payments app forces it on (CapabilityResolver
    // invariant). When the resolved profile is api-only, this posture reaches EVERY agent so none specs
    // Cosmos/RepositoryBase for a stateless app — the FullStack Scaffold Contract still describes the DB
    // seam (the shell physically ships it), so this OVERRIDES that for an api-only app. See
    // docs/roadmap/fullstack-capability-derivation.md.
    private const string ApiOnlyPostureInstructions = """
        This application is API-ONLY: it has a backend API but NO DATABASE and NO persistence. The
        resolved capability profile is api-only — there is nowhere to store data between requests.

        THIS OVERRIDES ANY CONTRARY INSTRUCTION in the request, the charter, the Scaffold Contract, the
        Definition of Done, or any earlier document. If anything mentions Cosmos, CosmosClient,
        RepositoryBase, a database/container, or saving / loading / listing / persisting records, treat
        it as OBSOLETE for this app: do NOT extend RepositoryBase, do NOT inject CosmosClient, do NOT add
        any datastore, and do NOT design features that depend on data surviving between requests. Keep
        the API stateless — validate, compute, transform, call any allowed integration, and return the
        result. Plan, design, and verify this as an API-only application with no persistence.
        """;

    // Form Capture (static apps): when Yorrixx has provisioned a Web3Forms key and will substitute it at
    // deploy, it sets formCaptureEnabled. The implementer wires forms to Web3Forms using a DEPLOY-
    // SUBSTITUTED PLACEHOLDER — the literal key never travels through the model (so prompt redaction
    // can't scrub it, and the key stays Yorrixx-owned). See docs/roadmap/static-design-quality.md.
    private const string FormCaptureInstructions = """
        A hosted Form Capture service (Web3Forms) is available — wire EVERY form on this static page to
        it so submissions are captured (a static page has no other backend). After client-side
        validation, submit via fetch (POST) to https://api.web3forms.com/submit with a JSON body
        containing "access_key": "__WEB3FORMS_ACCESS_KEY__" — write that EXACT placeholder; the deploy
        substitutes the real key, so NEVER invent, hardcode, or guess a key — plus all user fields and a
        "subject" and "from_name" derived from the brand. Send headers 'Content-Type: application/json'
        and 'Accept: application/json'. Parse the JSON response: on { "success": true } show the
        accessible success confirmation and reset the form; otherwise show an error message. Include a
        hidden honeypot input named "botcheck" (empty) for spam protection. Acceptance tests stay
        render-only: assert the form and its success affordance render; never POST a form in a test.
        """;

    private const string CapabilityGapsPreamble =
        "The architecture step found capabilities the brief may need that the user did NOT explicitly " +
        "enable. Per policy the explicit choice is HONORED (nothing is auto-added) — surface these for " +
        "the human to confirm or amend; do not silently wire them:\n\n";

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

        // Derive-once-stamp: the orchestrator carries the stamped profile from `.yorrixx/profile.json`.
        // Only an explicit "Static" applies; absent / "FullStack" leaves today's behaviour untouched.
        if (string.Equals(ReadMeta(context, "stackProfile"), "Static", StringComparison.OrdinalIgnoreCase))
            contextDocs[StackProfilePostureDocumentName] = StaticProfilePosture;

        // Capability profile — persistence axis. The orchestrator stamps needsDatabase only for FullStack
        // apps (a Static app has no API/DB and is covered above). Only an explicit "false" (resolver said
        // api-only) injects the no-database posture; absent / "true" leaves the DB-backed default intact.
        if (string.Equals(ReadMeta(context, "needsDatabase"), "false", StringComparison.OrdinalIgnoreCase))
            contextDocs[CapabilityPostureDocumentName] = ApiOnlyPostureInstructions;

        // Honored-but-flagged capability gaps (e.g. payments on, email off) — surfaced for the human.
        var capabilityGaps = ReadMeta(context, "capabilityGaps");
        if (!string.IsNullOrWhiteSpace(capabilityGaps))
            contextDocs[CapabilityGapsDocumentName] = CapabilityGapsPreamble + capabilityGaps;

        // Form Capture: Yorrixx sets formCaptureEnabled once it has provisioned a Web3Forms key and will
        // substitute the placeholder at deploy. Held until that provisioning ships (inert otherwise).
        if (string.Equals(ReadMeta(context, "formCaptureEnabled"), "true", StringComparison.OrdinalIgnoreCase))
            contextDocs[FormCapturePostureDocumentName] = FormCaptureInstructions;

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
