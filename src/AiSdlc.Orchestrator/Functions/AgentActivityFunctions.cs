using System.Text;
using System.Text.Json;
using AiSdlc.Agents;
using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.ModelProviders;
using AiSdlc.Orchestrator.Imagery;
using AiSdlc.RepoIndex;
using AiSdlc.RepoIndex.Charter;
using AiSdlc.Shared;
using AiSdlc.Shared.AutoMerge;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

public sealed class AgentActivityFunctions
{
    // Truncation guards keep individual audit-event properties under Azure Table Storage's
    // 64 KB-per-property cap (chosen well below to leave headroom for UTF-16 expansion).
    private const int MaxSummaryLength    = 256;
    private const int MaxStackTraceLength = 30_000;

    private readonly IAgentRunner _agentRunner;
    private readonly IGitHubService _gitHub;
    private readonly IRepoIndexer _repoIndexer;
    private readonly ICharterReader _charterReader;
    private readonly IAutoMergeEligibilityService _autoMergeEligibility;
    private readonly IContextStore _contextStore;
    private readonly IAuditService _audit;
    private readonly IBlobPromptStore _promptStore;
    private readonly IModelProvider _model;
    private readonly IImageSource _images;
    private readonly ILogger<AgentActivityFunctions> _logger;

    public AgentActivityFunctions(
        IAgentRunner agentRunner,
        IGitHubService gitHub,
        IRepoIndexer repoIndexer,
        ICharterReader charterReader,
        IAutoMergeEligibilityService autoMergeEligibility,
        IContextStore contextStore,
        IAuditService audit,
        IBlobPromptStore promptStore,
        IModelProvider model,
        IImageSource images,
        ILogger<AgentActivityFunctions> logger)
    {
        _agentRunner          = agentRunner;
        _gitHub               = gitHub;
        _repoIndexer          = repoIndexer;
        _charterReader        = charterReader;
        _autoMergeEligibility = autoMergeEligibility;
        _contextStore         = contextStore;
        _audit                = audit;
        _promptStore          = promptStore;
        _model                = model;
        _images               = images;
        _logger               = logger;
    }

    [Function(nameof(RunProductStrategistAsync))]
    public Task<AgentResult> RunProductStrategistAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ProductStrategist, context, cancellationToken);

    [Function(nameof(RunProductOwnerAsync))]
    public Task<AgentResult> RunProductOwnerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ProductOwner, context, cancellationToken);

    [Function(nameof(RunBusinessAnalystAsync))]
    public Task<AgentResult> RunBusinessAnalystAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.BusinessAnalyst, context, cancellationToken);

    [Function(nameof(RunArchitectAsync))]
    public Task<AgentResult> RunArchitectAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.Architect, context, cancellationToken);

    [Function(nameof(RunUxAccessibilityReviewerAsync))]
    public Task<AgentResult> RunUxAccessibilityReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.UxAccessibilityReviewer, context, cancellationToken);

    [Function(nameof(RunContentSeoReviewerAsync))]
    public Task<AgentResult> RunContentSeoReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ContentSeoReviewer, context, cancellationToken);

    [Function(nameof(RunDataAnalyticsReviewerAsync))]
    public Task<AgentResult> RunDataAnalyticsReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.DataAnalyticsReviewer, context, cancellationToken);

    [Function(nameof(RunComplianceLegalReviewerAsync))]
    public Task<AgentResult> RunComplianceLegalReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ComplianceLegalReviewer, context, cancellationToken);

    [Function(nameof(RunSecurityPrivacyReviewerAsync))]
    public Task<AgentResult> RunSecurityPrivacyReviewerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.SecurityPrivacyReviewer, context, cancellationToken);

    [Function(nameof(RunDevOpsPlatformEngineerAsync))]
    public Task<AgentResult> RunDevOpsPlatformEngineerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.DevOpsPlatformEngineer, context, cancellationToken);

    [Function(nameof(RunQaTestEngineerAsync))]
    public Task<AgentResult> RunQaTestEngineerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.QaTestEngineer, context, cancellationToken);

    [Function(nameof(RunSeniorCoderAsync))]
    public Task<AgentResult> RunSeniorCoderAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.SeniorCoder, context, cancellationToken);

    [Function(nameof(RunRiskAssessorAsync))]
    public Task<AgentResult> RunRiskAssessorAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.RiskAssessor, context, cancellationToken);

    [Function(nameof(RunReleaseManagerAsync))]
    public Task<AgentResult> RunReleaseManagerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.ReleaseManager, context, cancellationToken);

    [Function(nameof(PostGitHubCommentAsync))]
    public async Task PostGitHubCommentAsync([ActivityTrigger] PostCommentInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Posting comment to {Repository}#{Issue}", input.Repository, input.IssueNumber);

        var markdown = input.Markdown;
        if (input.ContentRefs is { Count: > 0 })
        {
            var resolved = await Task.WhenAll(input.ContentRefs.Select(async kv =>
                (Sentinel: kv.Key, Content: await _contextStore.ResolveAsync(kv.Value, cancellationToken))));
            foreach (var (sentinel, content) in resolved)
                markdown = markdown.Replace(sentinel, content, StringComparison.Ordinal);
        }

        markdown = TruncateForGitHub(markdown);

        var posted = await _gitHub.AddIssueCommentAsync(input.Repository, input.IssueNumber, markdown, cancellationToken);

        // Surface the comment URL in audit so the dashboard can link directly to it from the live feed.
        var summary = ExtractCommentHeading(markdown) ?? $"Comment posted on issue #{input.IssueNumber}";
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = BuildAuditRunId(input.Repository, input.IssueNumber),
                Repository  = input.Repository,
                IssueNumber = input.IssueNumber,
                ActorType   = "Comment",
                ActorName   = "GitHubComment",
                Action      = "Posted",
                Summary     = summary,
                References  = new Dictionary<string, string>
                {
                    ["commentUrl"] = posted.Url,
                    ["commentId"]  = posted.CommentId.ToString()
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write Comment audit event for {Repository}#{Issue}.", input.Repository, input.IssueNumber);
        }
    }

    private static string BuildAuditRunId(string repository, int issueNumber) =>
        $"{repository.Replace('/', '_')}_{issueNumber}";

    // GitHub rejects issue-comment bodies over 65,536 chars with a 422, which would fail the
    // activity AFTER the expensive agent work already succeeded (observed with chunked code
    // generation on 2026-06-11). Comments are informational — downstream parsing reads agent
    // output from the context store, never from the comment — so truncation is always safe.
    internal const int GitHubCommentMaxChars = 65536;

    private const string TruncationNotice =
        "\n\n---\n*(Comment truncated — it exceeded GitHub's 65,536-character limit. " +
        "The full content is preserved in the run's artefact store and dashboard.)*";

    internal static string TruncateForGitHub(string markdown) =>
        markdown.Length <= GitHubCommentMaxChars
            ? markdown
            : markdown[..(GitHubCommentMaxChars - TruncationNotice.Length)] + TruncationNotice;

    // The orchestrator builds markdown like "## AI SDLC — Specialist Reviews\n…" — return that
    // heading so the audit row reads naturally in the live feed.
    private static string? ExtractCommentHeading(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var firstLine = markdown.AsSpan().Trim();
        var newline = firstLine.IndexOf('\n');
        if (newline >= 0) firstLine = firstLine[..newline];
        var trimmed = firstLine.TrimStart('#').Trim();
        return trimmed.Length == 0 ? null : trimmed.ToString();
    }

    [Function(nameof(ResolveContextAsync))]
    public Task<string> ResolveContextAsync([ActivityTrigger] string contextRef, CancellationToken cancellationToken)
        => _contextStore.ResolveAsync(contextRef, cancellationToken);

    [Function(nameof(EmitBootstrapTerminalMarkerAuditAsync))]
    public async Task EmitBootstrapTerminalMarkerAuditAsync(
        [ActivityTrigger] BootstrapTerminalMarkerAuditInput input, CancellationToken cancellationToken)
    {
        // Typed audit counterpart of the HTML-comment terminal marker from PR #51.
        // ADR-0004 has both fire in v1 — the API consumes this; markers stay as the GitHub-side fallback.
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = BuildAuditRunId(input.Repository, input.IssueNumber),
                Repository  = input.Repository,
                IssueNumber = input.IssueNumber,
                ActorType   = "Workflow",
                ActorName   = "Orchestrator",
                Action      = "BootstrapTerminalMarker",
                Summary     = $"Bootstrap run {input.Status}.",
                Decision    = input.Status
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write BootstrapTerminalMarker audit event for {Repo}#{Issue}.", input.Repository, input.IssueNumber);
        }
    }

    [Function(nameof(RecordWorkflowExitAsync))]
    public async Task RecordWorkflowExitAsync(
        [ActivityTrigger] WorkflowExitAuditInput input, CancellationToken cancellationToken)
    {
        // Writes a single Workflow-actor audit event when the orchestrator exits early as Stopped or Failed.
        // The dashboard reads this to flip the run's status chip and mark downstream agents as Skipped.
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = BuildAuditRunId(input.Repository, input.IssueNumber),
                Repository  = input.Repository,
                IssueNumber = input.IssueNumber,
                ActorType   = "Workflow",
                ActorName   = "Orchestrator",
                Action      = input.Outcome,
                Summary     = input.Reason,
                Decision    = input.Outcome
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write workflow-exit audit event for {Repo}#{Issue}.", input.Repository, input.IssueNumber);
        }
    }

    [Function(nameof(FetchRepoIndexAsync))]
    public async Task<AiSdlc.RepoIndex.RepoIndex?> FetchRepoIndexAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching repo index for {Repository}", repository);
        var index = await _repoIndexer.IndexAsync(repository, cancellationToken);
        if (index is null)
            _logger.LogInformation("No .ai-sdlc.yml found in {Repository} — skipping repo index.", repository);
        return index;
    }

    [Function(nameof(FetchCharterAsync))]
    public async Task<Charter?> FetchCharterAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching .yorrixx/charter.json for {Repository}", repository);
        var charter = await _charterReader.ReadAsync(repository, cancellationToken);
        if (charter is null)
            _logger.LogInformation("No usable charter found in {Repository} — skipping charter.", repository);
        return charter;
    }

    internal const string StackProfilePath = ".yorrixx/profile.json";

    // Reads the Yorrixx-stamped stack profile (derive-once-stamp: Yorrixx derives at seed time and
    // stamps .yorrixx/profile.json; the platform reads it and never re-derives). Drives the Static
    // posture/contract (stack-profiles-static-first.md). Absent/malformed → "FullStack" (today's path).
    [Function(nameof(FetchStackProfileAsync))]
    public async Task<string> FetchStackProfileAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        var json = await _gitHub.GetFileContentAsync(repository, StackProfilePath, cancellationToken);
        var profile = ParseStackProfile(json);
        _logger.LogInformation("Stack profile for {Repository}: {Profile}", repository, profile);
        return profile;
    }

    // Only an explicit stackProfile == "Static" applies; an absent or malformed file defaults to
    // "FullStack". Key/value matching is case-insensitive so the stamp shape can't drift on casing.
    internal static string ParseStackProfile(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "FullStack";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "stackProfile", StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.String
                        && string.Equals(prop.Value.GetString(), "Static", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Static";
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed profile.json → safe default.
        }

        return "FullStack";
    }

    private const string DatabaseNeedSystemPrompt = """
        You are the platform Architect deciding ONE thing about a FullStack app: does it need a DATABASE
        (server-side persistence), or can it run API-ONLY (stateless)?

        Lean BALANCED: if the stated features plausibly involve saving, listing, editing, or retrieving
        user or content records that must survive between requests, it needs a database. A purely
        stateless app — a calculator, a unit/format converter, a one-shot generator, or a contact form
        with no stored history — does not.

        Output ONLY compact JSON, nothing else: {"database": true|false, "rationale": "one line"}
        """;

    // Balanced "does this app need persistence?" judgment (api-only vs api+db) — the agent-derived axis
    // of the capability profile (fullstack-capability-derivation.md). The orchestrator applies the hard
    // invariants afterwards (payments ⟹ database) via CapabilityResolver. Any failure defaults to
    // database=true — the safe, status-quo answer that never silently drops persistence.
    [Function(nameof(DeriveDatabaseNeedAsync))]
    public async Task<bool> DeriveDatabaseNeedAsync([ActivityTrigger] string charterMarkdown, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _model.CompleteAsync(new ModelRequest
            {
                AgentName    = AgentNames.Architect,
                TaskType     = "CapabilityDatabaseNeed",
                SystemPrompt = DatabaseNeedSystemPrompt,
                UserPrompt   = string.IsNullOrWhiteSpace(charterMarkdown) ? "(no charter provided)" : charterMarkdown,
                MaxTokens    = 300
            }, cancellationToken);

            var needsDatabase = ParseDatabaseDecision(response.ResponseText);
            _logger.LogInformation("Capability: database need derived as {NeedsDatabase}", needsDatabase);
            return needsDatabase;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database-need derivation failed; defaulting to database=true (safe).");
            return true;
        }
    }

    // Parses {"database": bool} from the model response; any ambiguity or malformed output defaults to
    // true (keep persistence — never silently drop a datastore the app might need).
    internal static bool ParseDatabaseDecision(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return true;
        var start = responseText.IndexOf('{');
        var end   = responseText.LastIndexOf('}');
        if (start < 0 || end <= start) return true;

        try
        {
            using var doc = JsonDocument.Parse(responseText[start..(end + 1)]);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("database", out var db))
            {
                if (db.ValueKind == JsonValueKind.False) return false;
                if (db.ValueKind == JsonValueKind.True) return true;
            }
        }
        catch (JsonException)
        {
            // Malformed → safe default.
        }

        return true;
    }

    private const string ImageryPlanSystemPrompt = """
        You are a design director deciding whether REAL PHOTOGRAPHY would elevate this app's marketing
        page, or whether generative CSS/SVG visuals are the stronger, more premium choice.

        DEFAULT TO NO. Say yes ONLY when a human / lifestyle / emotional image would meaningfully improve
        THIS brand — for example a coffee brand (a warm lifestyle moment), a dentist (genuine bright
        smiles), a yoga studio (people mid-practice). Say NO for abstract, data, B2B-tool, or severe-
        minimalist brands, where stock people cheapen it and generative visuals sell it better.

        Output ONLY compact JSON, nothing else:
        {"useImagery": true|false, "queries": ["literal stock-photo search query", ...], "rationale": "one line"}
        - If useImagery is false: queries is an empty array.
        - If true: 1-3 focused queries describing the literal photo you want (e.g. "woman relaxing with
          coffee at home", "close-up bright natural smile"). Tasteful and on-brand, never generic.
        """;

    // Selective real photography: a design-director judgment (default no) decides whether a photo lifts
    // THIS brand, then real Pexels URLs are fetched for the chosen queries. Returns a manifest the
    // implementer embeds, or "" to stay generative. Any failure (or no key configured) → "" (generative).
    // See docs/roadmap/static-design-quality.md §4.
    [Function(nameof(DeriveImageryAsync))]
    public async Task<string> DeriveImageryAsync([ActivityTrigger] string charterMarkdown, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await _model.CompleteAsync(new ModelRequest
            {
                AgentName    = AgentNames.UxAccessibilityReviewer,
                TaskType     = "ImageryPlan",
                SystemPrompt = ImageryPlanSystemPrompt,
                UserPrompt   = string.IsNullOrWhiteSpace(charterMarkdown) ? "(no charter provided)" : charterMarkdown,
                MaxTokens    = 400
            }, cancellationToken);

            var (useImagery, queries) = ParseImageryPlan(plan.ResponseText);
            if (!useImagery)
                return string.Empty;

            var manifest = await _images.BuildManifestAsync(queries, cancellationToken);
            _logger.LogInformation("Imagery: {Decision} ({Queries})",
                manifest is null ? "requested but none found/disabled" : "enabled", string.Join(", ", queries));
            return manifest ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imagery derivation failed; staying generative-only.");
            return string.Empty;
        }
    }

    // Parses {"useImagery": bool, "queries": [...]} ; any ambiguity / malformed output → (false, []).
    internal static (bool UseImagery, IReadOnlyList<string> Queries) ParseImageryPlan(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return (false, []);
        var start = responseText.IndexOf('{');
        var end   = responseText.LastIndexOf('}');
        if (start < 0 || end <= start) return (false, []);

        try
        {
            using var doc = JsonDocument.Parse(responseText[start..(end + 1)]);
            var root = doc.RootElement;
            var use = root.TryGetProperty("useImagery", out var u) && u.ValueKind == JsonValueKind.True;
            var queries = new List<string>();
            if (use && root.TryGetProperty("queries", out var q) && q.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in q.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) queries.Add(s!.Trim());
                }
            }
            return (use && queries.Count > 0, queries);
        }
        catch (JsonException)
        {
            return (false, []);
        }
    }

    [Function(nameof(AddGitHubLabelAsync))]
    public async Task AddGitHubLabelAsync([ActivityTrigger] AddLabelInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding label '{Label}' to {Repository}#{Issue}", input.Label, input.Repository, input.IssueOrPrNumber);
        await _gitHub.AddLabelsAsync(input.Repository, input.IssueOrPrNumber, [input.Label], cancellationToken);
    }

    [Function(nameof(GetCheckRunsStateAsync))]
    public async Task<ChecksState> GetCheckRunsStateAsync([ActivityTrigger] GetPrContextInput input, CancellationToken cancellationToken)
    {
        var checks = await _gitHub.GetCheckRunResultsAsync(input.Repository, input.HeadSha, cancellationToken);
        return new ChecksState(
            Total:       checks.Count,
            Pending:     checks.Count(c => c.Status != "completed"),
            FailedNames: checks
                .Where(c => GitHubApiClient.IsFailedCheck(c.Status, c.Conclusion))
                .Select(c => c.Name)
                .ToList());
    }

    [Function(nameof(GetNewestOpenAiPrAsync))]
    public Task<OpenPullRequestInfo?> GetNewestOpenAiPrAsync([ActivityTrigger] string repository, CancellationToken cancellationToken) =>
        _gitHub.GetNewestOpenPullRequestByBranchPrefixAsync(repository, "ai/", cancellationToken);

    //   .github/      — CI/CD workflows (violation #98: a repair edited deploy.yml)
    //   tests/e2e/    — the verification harness the release gate runs (auth.spec.ts, helpers,
    //                   playwright.config.ts) EXCEPT acceptance.spec.ts (authored once, see below)
    //   src/api/Auth/ — the immutable Clerk auth shell (ClerkJwtMiddleware, ClerkTokenValidator)
    private static readonly string[] ProtectedPathPrefixes = [".github/", "tests/e2e/", "src/api/Auth/"];

    // Scaffold-first (#131): the immutable app shell copied from the template repo
    // (kcsnap/ai-sdlc-react-dotnet-template). The Code Implementer fills feature slots (routes.tsx,
    // nav.ts, theme.ts, features/**, Features/**, Features/FeatureRegistration.cs) but must never
    // author the shell — that is what keeps auth, the build, and the DI seam intact.
    // File-level (not prefix-level) on the api side: the sample `items` feature
    // (Data/CosmosItemStore.cs, Functions/ItemsFunction.cs) co-locates with infra under Data/ and
    // Functions/ and must stay AI-replaceable, so only the specific infra files are pinned.
    private static readonly string[] ShellScaffoldFiles =
    [
        "src/frontend/src/main.tsx",
        "src/frontend/src/app/AppShell.tsx",
        "src/frontend/src/lib/api.ts",
        "src/frontend/src/vite-env.d.ts",
        "src/api/Program.cs",
        "src/api/Data/CosmosClientFactory.cs",
        "src/api/Functions/HealthFunction.cs",
        "src/api/host.json",
        "src/api/Api.csproj",
    ];

    // acceptance.spec.ts is the ONE verification-harness file the Code Implementer legitimately
    // authors — on the first build (replacing seeded stubs) and, on a repair, MAINTAINS without
    // gutting. So it is allowed through IsProtectedPath, and the repair filter guards it against
    // regression rather than freezing it (see IsAcceptanceSpecRegression).
    internal static bool IsAcceptanceSpec(string path) =>
        path.EndsWith("tests/e2e/specs/acceptance.spec.ts", StringComparison.OrdinalIgnoreCase);

    // Always immutable — never authored or repaired. Seeded by the template repo + drift-restored
    // by Yorrixx: the CI/CD + e2e harness (ProtectedPathPrefixes) and the app shell
    // (ShellScaffoldFiles), EXCEPT acceptance.spec.ts which the platform authors once.
    internal static bool IsProtectedPath(string path) =>
        !IsAcceptanceSpec(path) &&
        (ProtectedPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
         || ShellScaffoldFiles.Any(f => PathMatchesShellFile(path, f)));

    // Manifest/parser paths are repo-root-relative; tolerate an optional leading "./".
    private static bool PathMatchesShellFile(string path, string shellFile) =>
        path.Equals(shellFile, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/" + shellFile, StringComparison.OrdinalIgnoreCase);

    // acceptance.spec.ts is NOT frozen on repair (#117 refined per Yorrixx): the platform authors
    // it on the first build and may MAINTAIN it on a repair (e.g. fix a selector / register helper).
    // What it must never do is GUT it — the #115 incident, where a vague finding made a repair delete
    // AC1–AC7. So a repair's change to acceptance.spec.ts is allowed only when it is NON-REGRESSIVE vs
    // the existing file. If the existing content can't be verified, treat it as a regression (block) —
    // the safe default that preserves the original #115 protection.
    internal static bool IsAcceptanceSpecRegression(string? existing, string proposed)
    {
        if (string.IsNullOrWhiteSpace(existing)) return true; // can't verify on a repair → block
        static int Count(string s, string pattern) =>
            System.Text.RegularExpressions.Regex.Matches(s, pattern).Count;
        // Gutting signatures: fewer tests, fewer assertions, more skipped/only, or more throws.
        return Count(proposed, @"\btest\s*\(")              < Count(existing, @"\btest\s*\(")
            || Count(proposed, @"\bexpect\s*\(")            < Count(existing, @"\bexpect\s*\(")
            || Count(proposed, @"\.(skip|only)\b")          > Count(existing, @"\.(skip|only)\b")
            || Count(proposed, @"\bthrow\b")                > Count(existing, @"\bthrow\b");
    }

    /// <summary>
    /// Repairs must be minimal diffs against the findings, never refactors. Keep only files
    /// the findings implicate (full path or filename mentioned); if that would drop every
    /// file, fall back to the unfiltered set (minus protected paths) rather than bricking
    /// the repair — the findings text may reference files indirectly. acceptance.spec.ts may be
    /// maintained but never gutted (see IsAcceptanceSpecRegression); pass its existing content
    /// so a gutting change is dropped.
    /// </summary>
    internal static List<FileChange> FilterRepairChanges(
        IReadOnlyList<FileChange> changes, string findingsText, string? existingAcceptanceSpec = null)
    {
        // Immutable harness (.github/, tests/e2e/ except acceptance.spec.ts) is always dropped.
        // acceptance.spec.ts is dropped only when the change would regress it (or can't be verified).
        var allowed = changes.Where(c =>
            !IsProtectedPath(c.Path) &&
            !(IsAcceptanceSpec(c.Path) && IsAcceptanceSpecRegression(existingAcceptanceSpec, c.Content)))
            .ToList();
        var implicated = allowed.Where(c => IsImplicatedByFindings(c.Path, findingsText)).ToList();
        return implicated.Count > 0 ? implicated : allowed;
    }

    internal static bool IsImplicatedByFindings(string path, string findingsText)
    {
        if (string.IsNullOrEmpty(findingsText)) return false;
        if (findingsText.Contains(path, StringComparison.OrdinalIgnoreCase))
            return true;
        var fileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        return findingsText.Contains(fileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when this run is a repair (reopen verification findings or in-run CI findings) over
    /// existing source — the orchestrator uses this to apply <see cref="FilterRepairChanges"/> to
    /// the implementer output, mirroring the agent-side gate
    /// (<c>CodeImplementerAgent.IsRepairRequest</c>). Without it, reopen-driven repairs commit
    /// their output unfiltered and can smuggle in a full regeneration.
    /// </summary>
    internal static bool IsRepairRun(IReadOnlyDictionary<string, object> metadata)
    {
        static bool Has(IReadOnlyDictionary<string, object> m, string key) =>
            m.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(Convert.ToString(v));
        return (Has(metadata, "reopenFindings") || Has(metadata, "ciFindings")) && Has(metadata, "existingSource");
    }

    // Caps keep the findings prompt-sized; truncation drops whole-check sections rather
    // than splitting one mid-error.
    internal const int CiFindingsMaxChars         = 15_000;
    internal const int CiFindingsPerCheckMaxChars = 6_000;

    [Function(nameof(FetchCiFailureFindingsAsync))]
    public async Task<string> FetchCiFailureFindingsAsync([ActivityTrigger] FetchCiFindingsInput input, CancellationToken cancellationToken)
    {
        var findings = await _gitHub.GetFailedCheckFindingsAsync(input.Repository, input.HeadSha, cancellationToken);
        var rendered = RenderCiFindings(findings);
        if (string.IsNullOrWhiteSpace(rendered))
            return string.Empty; // nothing actionable — the orchestrator must not repair blind

        _logger.LogInformation("Extracted CI findings for {Repository}@{Sha} (attempt {Attempt}): {Chars} chars from {Checks} failed check(s).",
            input.Repository, input.HeadSha, input.Attempt, rendered.Length, findings.Count);
        return await _contextStore.OffloadAsync(input.RunId, $"ci-findings-attempt-{input.Attempt}", rendered, cancellationToken);
    }

    internal static string RenderCiFindings(IReadOnlyList<FailedCheckFinding> findings)
    {
        var sections = new List<string>();
        foreach (var finding in findings)
        {
            // Only substantive annotations can guide a repair; runner-generated ones
            // ("Process completed with exit code 1") would render as findings while
            // saying nothing — prefer the log tail in that case.
            var substantive = finding.Annotations
                .Where(a => GitHubApiClient.IsSubstantiveAnnotation(a.Level, a.Message))
                .ToList();

            string body;
            if (substantive.Count > 0)
            {
                body = string.Join('\n', substantive
                    .Select(a => $"{a.Path}:{a.StartLine} [{a.Level}] {a.Message}"));
            }
            else if (!string.IsNullOrWhiteSpace(finding.LogTail))
            {
                body = $"```\n{finding.LogTail}\n```";
            }
            else
            {
                continue; // nothing extractable for this check
            }

            if (body.Length > CiFindingsPerCheckMaxChars)
                body = body[..CiFindingsPerCheckMaxChars];

            sections.Add($"## Check: {finding.CheckName}\n\n{body}");
        }

        var output = string.Join("\n\n", sections);
        while (output.Length > CiFindingsMaxChars && sections.Count > 1)
        {
            sections.RemoveAt(0); // drop whole-check sections from the front
            output = string.Join("\n\n", sections);
        }
        return output.Length <= CiFindingsMaxChars ? output : output[..CiFindingsMaxChars];
    }

    // Bounded so a big repo cannot blow the prompt: skip generated/binary/oversized files
    // and stop at a cumulative cap. The bundle is offloaded to the context store and the
    // REF is returned — agent execution resolves metadata refs before building prompts.
    internal const int RepairSourceMaxFileBytes  = 40_000;
    // Large enough to hold a whole generated user-app: a 160KB cap handed the repair a partial
    // codebase, which the model then "completed" by regenerating the missing files — namespace
    // drift and hallucinated deps followed (#100 evidence: 412-error reopen regen on 624d97a2).
    internal const int RepairSourceTotalBudget   = 512_000;

    private static readonly string[] RepairSourceExcludedExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".woff", ".woff2", ".ttf", ".zip", ".dll", ".pdf", ".map"];

    private static readonly string[] RepairSourceExcludedNames = ["package-lock.json", "yarn.lock", "pnpm-lock.yaml"];

    private static readonly string[] RepairSourceExcludedPrefixes = ["node_modules/", "dist/", "bin/", "obj/", ".git/", ".github/"];

    [Function(nameof(FetchExistingSourceAsync))]
    public async Task<string> FetchExistingSourceAsync([ActivityTrigger] FetchExistingSourceInput input, CancellationToken cancellationToken)
    {
        // Reopen repairs read the released code (default branch); in-run CI repairs read the
        // WORK branch — the failing code lives there. Blob key reuse is safe: fetches are
        // strictly sequential and the ref is resolved before any later overwrite.
        var branch = input.Branch ?? await _gitHub.GetDefaultBranchAsync(input.Repository, cancellationToken);
        var tree   = await _gitHub.GetBranchFileTreeAsync(input.Repository, branch, cancellationToken);
        var paths  = SelectRepairSourcePaths(tree, input.FindingsText);

        var fetched = await Task.WhenAll(paths.Select(async p =>
            (Path: p, Content: await _gitHub.GetBranchFileContentAsync(input.Repository, p, branch, cancellationToken))));

        var sb = new StringBuilder();
        foreach (var (path, content) in fetched)
        {
            if (content is null) continue;
            sb.Append("<file path=\"").Append(path).AppendLine("\">");
            sb.AppendLine(content);
            sb.AppendLine("</file>");
        }

        if (sb.Length == 0)
            return string.Empty;

        _logger.LogInformation("Bundled {Count} existing source files ({Bytes} chars) from {Repository}@{Branch} for repair.",
            paths.Count, sb.Length, input.Repository, branch);
        return await _contextStore.OffloadAsync(input.RunId, "existing-source", sb.ToString(), cancellationToken);
    }

    internal static IReadOnlyList<string> SelectRepairSourcePaths(
        IReadOnlyList<RepoTreeEntry> tree, string? findingsText = null)
    {
        static bool Eligible(RepoTreeEntry entry)
        {
            if (entry.Size > RepairSourceMaxFileBytes) return false;
            var fileName = entry.Path.Contains('/') ? entry.Path[(entry.Path.LastIndexOf('/') + 1)..] : entry.Path;
            if (RepairSourceExcludedNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)) return false;
            if (RepairSourceExcludedExtensions.Any(ext => entry.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return false;
            if (RepairSourceExcludedPrefixes.Any(prefix => entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return false;
            return true;
        }

        // Findings-implicated files first so the files the repair must touch are guaranteed in
        // the bundle even when the whole app exceeds the budget; OrderBy is stable, so ties keep
        // tree order and non-implicated files still fill the remaining budget.
        var eligible = tree.Where(Eligible);
        var ordered  = string.IsNullOrEmpty(findingsText)
            ? eligible
            : eligible.OrderByDescending(e => IsImplicatedByFindings(e.Path, findingsText));

        var selected = new List<string>();
        long budget = RepairSourceTotalBudget;
        foreach (var entry in ordered)
        {
            if (entry.Size > budget) continue;
            selected.Add(entry.Path);
            budget -= entry.Size;
        }

        return selected;
    }

    [Function(nameof(FetchReopenFindingsAsync))]
    public async Task<string> FetchReopenFindingsAsync([ActivityTrigger] FetchReopenFindingsInput input, CancellationToken cancellationToken)
    {
        var comments = await _gitHub.GetIssueCommentsAsync(input.Repository, input.IssueNumber, cancellationToken);
        return ExtractReopenFindings(comments);
    }

    // The findings are whatever was said AFTER the previous run finished: comments following
    // the last <!-- ai-sdlc:status= --> terminal marker, excluding the platform's own stage
    // comments. That captures Yorrixx's verification findings without depending on their format.
    internal static string ExtractReopenFindings(IReadOnlyList<IssueComment> comments)
    {
        const int MaxChars = 20000;

        var lastMarker = -1;
        for (var i = 0; i < comments.Count; i++)
        {
            if (comments[i].BodyMarkdown.Contains("<!-- ai-sdlc:status=", StringComparison.Ordinal))
                lastMarker = i;
        }

        var findings = comments
            .Skip(lastMarker + 1)
            .Where(c => !c.BodyMarkdown.StartsWith("## AI SDLC", StringComparison.Ordinal)
                     && !c.BodyMarkdown.Contains("<!-- ai-sdlc:", StringComparison.Ordinal))
            .Select(c => c.BodyMarkdown.Trim())
            .Where(body => body.Length > 0)
            .ToList();

        if (findings.Count == 0)
            return string.Empty;

        var joined = string.Join("\n\n---\n\n", findings);
        return joined.Length <= MaxChars ? joined : joined[..MaxChars];
    }

    [Function(nameof(GetPullRequestContextAsync))]
    public async Task<PrMergeContext> GetPullRequestContextAsync([ActivityTrigger] GetPrContextInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching PR context for {Repository}#{Pr}", input.Repository, input.PullRequestNumber);

        var prTask     = _gitHub.GetPullRequestAsync(input.Repository, input.PullRequestNumber, cancellationToken);
        var filesTask  = _gitHub.GetChangedFilesAsync(input.Repository, input.PullRequestNumber, cancellationToken);
        var checksTask = _gitHub.GetCheckRunResultsAsync(input.Repository, input.HeadSha, cancellationToken);

        await Task.WhenAll(prTask, filesTask, checksTask);

        var pr     = await prTask;
        var files  = await filesTask;
        var checks = await checksTask;

        var allChecksPass = checks.Count == 0
            || checks.All(c => c.Status == "completed" && c.Conclusion == "success");

        var isDocsOnly = files.Count > 0
            && files.All(f => f.Path.EndsWith(".md",  StringComparison.OrdinalIgnoreCase)
                           || f.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                           || f.Path.EndsWith(".rst", StringComparison.OrdinalIgnoreCase));

        var hasTestCoverage = isDocsOnly
            || checks.Any(c =>
                (c.Name.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                 c.Name.Contains("coverage", StringComparison.OrdinalIgnoreCase))
                && c.Conclusion == "success")
            || files.Any(f => f.Path.Contains("test", StringComparison.OrdinalIgnoreCase));

        return new PrMergeContext(
            PullRequestNumber: input.PullRequestNumber,
            HeadSha:           input.HeadSha,
            Mergeable:         pr.Mergeable,
            AllChecksPass:     allChecksPass,
            HasTestCoverage:   hasTestCoverage,
            ChangedFiles:      files);
    }

    [Function(nameof(EvaluateAutoMergeAsync))]
    public Task<AutoMergeEligibilityResult> EvaluateAutoMergeAsync([ActivityTrigger] EvaluateMergeInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Evaluating auto-merge gates for {Repository} run {RunId}", input.Repository, input.RunId);

        var result = _autoMergeEligibility.Evaluate(new AutoMergeEligibilityRequest
        {
            RunId                       = input.RunId,
            Repository                  = input.Repository,
            RiskLevel                   = input.RiskLevel,
            RiskDecision                = input.RiskDecision,
            BriefApproved               = input.BriefApproved,
            AllReviewsCompleted         = input.AllReviewsCompleted,
            NoBlockingIssues            = input.NoBlockingIssues,
            AllChecksPass               = input.AllChecksPass,
            HasTestCoverage             = input.HasTestCoverage,
            RollbackDocumented          = input.RollbackDocumented,
            ReleaseNotesGenerated       = input.ReleaseNotesGenerated,
            PostDeploymentChecksDefined  = input.PostDeploymentChecksDefined
        });

        return Task.FromResult(result);
    }

    [Function(nameof(MergePullRequestActivityAsync))]
    public async Task MergePullRequestActivityAsync([ActivityTrigger] MergePrInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Merging PR #{Pr} on {Repository}", input.PullRequestNumber, input.Repository);
        await _gitHub.MergePullRequestAsync(input.Repository, input.PullRequestNumber, input.CommitMessage, cancellationToken);
    }

    [Function(nameof(RunCodeImplementerAsync))]
    public Task<AgentResult> RunCodeImplementerAsync([ActivityTrigger] AgentContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(AgentNames.CodeImplementer, context, cancellationToken);

    [Function(nameof(GetDefaultBranchNameActivityAsync))]
    public async Task<string> GetDefaultBranchNameActivityAsync([ActivityTrigger] string repository, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting default branch name for {Repository}", repository);
        return await _gitHub.GetDefaultBranchAsync(repository, cancellationToken);
    }

    [Function(nameof(GetDefaultBranchShaActivityAsync))]
    public async Task<string> GetDefaultBranchShaActivityAsync([ActivityTrigger] GetHeadShaInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting HEAD SHA of {Branch} on {Repository}", input.Branch, input.Repository);
        return await _gitHub.GetDefaultBranchShaAsync(input.Repository, input.Branch, cancellationToken);
    }

    [Function(nameof(CreateBranchActivityAsync))]
    public async Task CreateBranchActivityAsync([ActivityTrigger] CreateBranchInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating branch {Branch} on {Repository} from {Sha}", input.BranchName, input.Repository, input.Sha);
        await _gitHub.CreateBranchAsync(input.Repository, input.BranchName, input.Sha, cancellationToken);
    }

    [Function(nameof(CommitFileAsync))]
    public async Task CommitFileAsync([ActivityTrigger] CommitFileInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Committing {Path} to {Branch} on {Repository}", input.Path, input.Branch, input.Repository);
        await _gitHub.CreateOrUpdateFileAsync(input.Repository, input.Path, input.Content, input.CommitMessage, input.Branch, cancellationToken);
    }

    [Function(nameof(CreatePrActivityAsync))]
    public async Task<GitHubPullRequestReference> CreatePrActivityAsync([ActivityTrigger] CreatePrActivityInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating PR '{Title}' on {Repository} from branch {Branch}", input.Title, input.Repository, input.BranchName);
        return await _gitHub.CreatePullRequestAsync(
            new CreatePullRequestRequest
            {
                Repository   = input.Repository,
                Title        = input.Title,
                BodyMarkdown = input.Body,
                HeadBranch   = input.BranchName,
                BaseBranch   = input.BaseBranch
            },
            cancellationToken);
    }

    [Function(nameof(ReviewBranchContentAsync))]
    public async Task<AgentResult> ReviewBranchContentAsync([ActivityTrigger] ReviewBranchInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Product Owner reviewing {FileCount} file(s) on branch {Branch} in {Repository}",
            input.FilePaths.Count, input.BranchName, input.Repository);

        var fetchTasks = input.FilePaths
            .Select(async path => (path, content: await _gitHub.GetBranchFileContentAsync(input.Repository, path, input.BranchName, cancellationToken)))
            .ToArray();
        var fetched = await Task.WhenAll(fetchTasks);

        var sb = new StringBuilder();
        foreach (var (path, content) in fetched)
        {
            if (content is not null)
            {
                sb.AppendLine($"### {path}");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        var context = new AgentContext
        {
            RunId          = input.RunId,
            Repository     = input.Repository,
            IssueNumber    = input.IssueNumber,
            CurrentState   = "Reviewing",
            RequestedAgent = AgentNames.ProductOwnerBranchReview,
            Metadata       =
            {
                ["branchContent"] = sb.ToString(),
                ["branchName"]    = input.BranchName,
                ["ownerBrief"]    = input.OwnerBrief,
                ["analystOutput"] = input.AnalystOutput,
                ["charter"]       = input.Charter
            }
        };

        return await ExecuteAsync(AgentNames.ProductOwnerBranchReview, context, cancellationToken);
    }

    private async Task<AgentResult> ExecuteAsync(string agentName, AgentContext context, CancellationToken cancellationToken)
    {
        await WriteAgentAuditAsync(agentName, context, action: "Started", summary: $"{agentName} started", cancellationToken: cancellationToken);

        AgentResult result;
        try
        {
            // Resolve blob references so the agent receives full content.
            // Metadata values are deserialized as JsonElement (not string) after Durable checkpointing,
            // so we must extract the string value from both types.
            foreach (var key in context.Metadata.Keys.ToList())
            {
                var strValue = context.Metadata[key] switch
                {
                    string s => s,
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
                        => je.GetString(),
                    _ => null
                };
                if (strValue is not null && _contextStore.IsReference(strValue))
                    context.Metadata[key] = await _contextStore.ResolveAsync(strValue, cancellationToken);
            }

            var executionResult = await _agentRunner.ExecuteAsync(
                new AgentExecutionRequest { AgentName = agentName, Context = context },
                cancellationToken);

            if (!executionResult.Succeeded || executionResult.Result is null)
                throw new InvalidOperationException(executionResult.ErrorMessage ?? $"Agent execution failed for '{agentName}'.");

            result = executionResult.Result;

            // Persist what the agent saw (input) and what it produced (output) to the prompts blob
            // so the dashboard's drill-down can render them. Done BEFORE the context-store offload
            // below so we capture the raw OutputMarkdown — the offload nulls it.
            await StoreAgentArtefactAsync(agentName, context, result, cancellationToken);

            // Offload outputs larger than 1 KB to blob; null OutputMarkdown so Durable history stays slim
            if (result.OutputMarkdown?.Length > 1024)
            {
                var reference = await _contextStore.OffloadAsync(context.RunId, agentName, result.OutputMarkdown, cancellationToken);
                result = new AgentResult
                {
                    AgentName         = result.AgentName,
                    Status            = result.Status,
                    Summary           = result.Summary,
                    OutputMarkdown    = null,
                    ContextRef        = reference,
                    Decision          = result.Decision,
                    RiskLevel         = result.RiskLevel,
                    ArtefactsCreated  = result.ArtefactsCreated,
                    FollowUpQuestions = result.FollowUpQuestions,
                    BlockingIssues    = result.BlockingIssues
                };
            }
        }
        catch (Exception ex)
        {
            await WriteAgentAuditAsync(
                agentName,
                context,
                action: "Failed",
                summary: Truncate(ex.Message, MaxSummaryLength),
                references: new Dictionary<string, string>
                {
                    ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["stackTrace"]    = Truncate(ex.ToString(), MaxStackTraceLength)
                },
                cancellationToken: CancellationToken.None);  // audit must still write even if the activity was cancelled
            throw;
        }

        await WriteAgentAuditAsync(
            agentName,
            context,
            action: "Completed",
            summary: result.Summary,
            decision: result.Decision,
            riskLevel: result.RiskLevel,
            cancellationToken: cancellationToken);

        return result;
    }

    private async Task WriteAgentAuditAsync(
        string agentName,
        AgentContext context,
        string action,
        string summary,
        string? decision = null,
        string? riskLevel = null,
        Dictionary<string, string>? references = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _audit.WriteAsync(new AuditEvent
            {
                RunId       = context.RunId,
                Repository  = context.Repository,
                IssueNumber = context.IssueNumber,
                PullRequestNumber = context.PullRequestNumber,
                ActorType   = "Agent",
                ActorName   = agentName,
                Action      = action,
                Summary     = summary,
                Decision    = decision,
                RiskLevel   = riskLevel,
                References  = references ?? new Dictionary<string, string>()
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit writes must never break agent execution — match the webhook handler's behaviour.
            _logger.LogWarning(ex, "Failed to write agent audit event {Action} for {Agent}.", action, agentName);
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    // Writes the agent's input (serialised metadata) + raw output to blob storage so the dashboard
    // can show them on the drill-down. Failures are logged but never fail the agent execution.
    private async Task StoreAgentArtefactAsync(
        string agentName, AgentContext context, AgentResult result, CancellationToken cancellationToken)
    {
        try
        {
            var input  = await SerialiseAgentInputAsync(context, cancellationToken);
            var output = result.OutputMarkdown ?? string.Empty;
            await _promptStore.StoreAsync(context.RunId, agentName, input, output, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to store agent artefact for {Agent} on run {RunId}.",
                agentName, context.RunId);
        }
    }

    // Renders AgentContext.Metadata as readable markdown — a heading per key, fenced code block for
    // multi-line values. Resolves context-store references (ctx:...) so the dashboard sees actual
    // content, not opaque blob references.
    private async Task<string> SerialiseAgentInputAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append("# Agent input — ").AppendLine(context.RequestedAgent);
        sb.AppendLine();
        sb.Append("- **Run:** ").AppendLine(context.RunId);
        sb.Append("- **Repository:** ").AppendLine(context.Repository);
        sb.Append("- **Issue:** #").Append(context.IssueNumber).AppendLine();
        if (context.PullRequestNumber is int pr)
        {
            sb.Append("- **PR:** #").Append(pr).AppendLine();
        }
        sb.Append("- **State:** ").AppendLine(context.CurrentState);
        sb.AppendLine();

        if (context.Metadata.Count == 0)
        {
            sb.AppendLine("_No metadata supplied._");
            return sb.ToString();
        }

        sb.AppendLine("## Metadata");
        foreach (var (key, rawValue) in context.Metadata.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var value = rawValue?.ToString();
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Resolve context-store references so the dashboard sees the actual offloaded text.
            if (_contextStore.IsReference(value))
            {
                try { value = await _contextStore.ResolveAsync(value, cancellationToken); }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not resolve context ref for {Key}", key); }
            }

            sb.AppendLine();
            sb.Append("### ").AppendLine(key);
            if (value!.Contains('\n'))
            {
                sb.AppendLine("```");
                sb.AppendLine(value);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine(value);
            }
        }

        return sb.ToString();
    }
}

public sealed record GetHeadShaInput(string Repository, string Branch);
public sealed record CreateBranchInput(string Repository, string BranchName, string Sha);
public sealed record CommitFileInput(string Repository, string Path, string Content,
                                     string CommitMessage, string Branch);
public sealed record CreatePrActivityInput(string Repository, string Title,
                                           string Body, string BranchName, string BaseBranch);
public sealed record ReviewBranchInput(
    string RunId,
    string Repository,
    int IssueNumber,
    string BranchName,
    IReadOnlyList<string> FilePaths,
    string OwnerBrief,
    string AnalystOutput,
    string Charter = "");
