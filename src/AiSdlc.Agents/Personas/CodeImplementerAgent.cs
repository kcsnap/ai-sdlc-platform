using System.Text;
using System.Text.RegularExpressions;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

/// <summary>One planned file in the implementation manifest.</summary>
public sealed record ManifestItem(string Path, string Purpose);

/// <summary>
/// Generates the implementation in chunks: a file manifest first, then the files in small
/// batches, each within its own model response. Single-shot generation truncated silently on
/// large apps — files referenced by the entrypoint were never emitted, and review failed the
/// run for incompleteness (issue #76). A manifest makes "referenced but missing" detectable:
/// files absent after a recovery pass fail the stage instead of committing a partial app.
/// </summary>
public sealed class CodeImplementerAgent : IAgent
{
    // Small enough that a batch of complete files fits comfortably in one response.
    internal const int BatchSize = 3;

    // Hard stop against a runaway manifest; the prompt steers well below this.
    internal const int ManifestFileCap = 60;

    // Per-batch output ceiling. Coupled files (e.g. a Static index.html + styles.css + app.js) must
    // fit complete in one response; at the old 8k ceiling they truncated, spilling siblings into the
    // recovery pass where they were authored blind to each other (the sport121 class-name desync).
    internal const int BatchMaxTokens = 16000;

    // Character budget for already-emitted sibling CONTENT injected into a batch prompt, so files are
    // authored against the real markup/structure of their siblings (matching class names, imports,
    // routes, DOM). Bounded so a large multi-file app cannot blow up the prompt — past the budget,
    // remaining siblings are listed by path only.
    internal const int EmittedContentBudgetChars = 24000;

    private const string ManifestSystemPrompt = """
        You are the Code Implementer planning stage in an AI-driven SDLC pipeline.

        Plan the set of FEATURE files needed to implement the request, on top of the fixed shell
        described in the Scaffold Contract. The shell already exists and compiles.

        Rules:
        - Output ONLY a manifest block — no prose, no explanation, nothing outside it:

          <manifest>
          <item path="relative/path/from/repo/root">one-line purpose of the file</item>
          </manifest>

        - Plan ONLY feature files. NEVER list a shell/infra file — they already exist and any you
          list will be discarded. Do NOT list: src/frontend/src/main.tsx, app/AppShell.tsx,
          lib/api.ts, vite-env.d.ts; src/api/Program.cs, src/api/Auth/**,
          src/api/Data/CosmosClientFactory.cs, src/api/Functions/HealthFunction.cs, host.json,
          Api.csproj; anything under .github/, and anything under tests/e2e/ EXCEPT
          tests/e2e/specs/acceptance.spec.ts.
        - DO list the feature slots you fill: src/frontend/src/app/routes.tsx,
          src/frontend/src/app/nav.ts, src/frontend/src/theme.ts, src/frontend/src/features/**,
          src/api/Features/** including src/api/Features/FeatureRegistration.cs, and
          tests/e2e/specs/acceptance.spec.ts (author it over the seeded throwing stubs).
        - List every feature file required for a complete, runnable implementation — if an import
          will reference a file, that file MUST be in the manifest.
        - Keep the design modular: prefer many focused files over few large ones.
        """;

    private const string BatchSystemPrompt = """
        You are the Code Implementer in an AI-driven SDLC pipeline.

        The implementation plan is already fixed. You will be told exactly which files to
        generate in this batch — generate those files COMPLETELY, consistent with the plan.

        Rules:
        - Output ONLY file blocks — no prose, no explanation, no text outside blocks.
        - For every file, use EXACTLY this format:

          <file path="relative/path/from/repo/root">
          (file content here)
          </file>

        - Paths are relative to the repository root (e.g. README.md, src/api/Controllers/Foo.cs).
        - Generate ONLY the files requested for this batch, each one complete — never truncate
          or stub a file with placeholders.
        - The literal text </file> must never appear inside file content.
        """;

    private const string SingleShotSystemPrompt = """
        You are the Code Implementer in an AI-driven SDLC pipeline.

        Write the actual files needed to implement the feature based on the brief,
        business analysis, architecture review, and implementation specification provided.

        Rules:
        - Output ONLY file blocks — no prose, no explanation, no text outside blocks.
        - For every file to create or modify, use EXACTLY this format:

          <file path="relative/path/from/repo/root">
          (file content here)
          </file>

        - Paths are relative to the repository root (e.g. README.md, src/api/Controllers/Foo.cs).
        - Output all files required to fully implement the feature.
        - Do not output anything outside the file blocks.
        - The literal text </file> must never appear inside file content.
        """;

    private const string RepairSystemPrompt = """
        You are the Code Implementer in REPAIR mode in an AI-driven SDLC pipeline.

        The application already exists but failed verification — either downstream
        verification after release, or the pull request's CI build. You are given the
        CURRENT source code and the findings (often exact compiler output). Your job is
        a surgical fix, not a rewrite.

        The application ALREADY BUILDS and is merged to main — only the listed findings are
        wrong. This is edit mode, not greenfield: never regenerate the app, and never re-emit
        a file the findings do not implicate.

        Rules:
        - Fix ONLY what the findings implicate. Do not redesign, restructure, rename, or
          "improve" anything else — unchanged files must not be touched.
        - NEVER rename namespaces, classes, files, or folders — a rename is a refactor,
          not a repair, and it breaks every other file that references the old name.
        - NEVER create new files unless an error explicitly requires one (e.g. a missing
          module the findings name).
        - NEVER touch the immutable shell or platform files (see the Scaffold Contract): anything
          under .github/ or tests/e2e/ (except tests/e2e/specs/acceptance.spec.ts), and the app
          shell — main.tsx, app/AppShell.tsx, lib/api.ts, vite-env.d.ts, src/api/Program.cs,
          src/api/Auth/**, Data/CosmosClientFactory.cs, Functions/HealthFunction.cs, host.json,
          Api.csproj. They are read-only.
        - Output ONLY the files that need to change, each as a complete file using EXACTLY:

          <file path="relative/path/from/repo/root">
          (complete fixed file content)
          </file>

        - Each output file must be the COMPLETE corrected file, based on the existing
          content shown to you — never a fragment or diff.
        - Output nothing outside the file blocks.
        - The literal text </file> must never appear inside file content.
        """;

    // Scaffold-first (#131): every user-app is created by copying the template repo
    // (kcsnap/ai-sdlc-react-dotnet-template), so a tested, compiling shell already exists — auth,
    // layout, the API client, the Cosmos client, and the DI seam are all wired. The Code
    // Implementer's job is to FILL FEATURE SLOTS, never to re-author the shell. This contract
    // replaces the old "describe how to build auth" doc, which still produced wrong imports
    // (@clerk/react) and RedirectToSignIn in the v004 baseline. It travels into every
    // generation/repair prompt and is enforced by IsProtectedPath on the orchestrator side.
    internal const string ScaffoldContractLabel = "Scaffold Contract (DO NOT BREAK)";

    private const string RetryPrompt =
        "Your previous response contained no `<file path=\"...\">` blocks. " +
        "You MUST wrap every file in `<file path=\"...\">` tags. " +
        "Output ONLY file blocks — nothing else.";

    private static readonly Regex ManifestItemRegex = new(
        @"<item path=""([^""]+)"">\s*([^<]*?)\s*</item>",
        RegexOptions.Compiled);

    private readonly IModelProvider _model;

    public CodeImplementerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.CodeImplementer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        // Repair mode: the app exists and failed verification (downstream reopen) or its
        // PR's CI build (in-run) — iterate on the existing code with the findings, never
        // regenerate. Regeneration cannot converge: each rewrite introduces fresh defects
        // in different files (#92, #95).
        if (IsRepairRequest(request.Context))
        {
            return await RepairAsync(contextDocs, userPrompt, request.Context.IssueNumber, cancellationToken);
        }

        var manifestResponse = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeImplementationManifest",
            SystemPrompt     = ManifestSystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2000
        }, cancellationToken);

        var manifest = ParseManifest(manifestResponse.ResponseText);

        // No parseable manifest (model ignored the format, or a fake/test provider) —
        // fall back to the original single-shot generation.
        if (manifest.Count == 0)
            return await SingleShotAsync(contextDocs, userPrompt, request.Context.IssueNumber, cancellationToken);

        if (manifest.Count > ManifestFileCap)
            throw new InvalidOperationException(
                $"Implementation manifest listed {manifest.Count} files (cap {ManifestFileCap}) — " +
                "refusing runaway generation. The implementation specification likely needs splitting.");

        var manifestText = string.Join("\n", manifest.Select(m => $"- {m.Path} — {m.Purpose}"));
        var emitted      = new Dictionary<string, FileChange>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in manifest.Chunk(BatchSize))
            await GenerateBatchAsync(batch, manifestText, contextDocs, userPrompt, emitted, cancellationToken);

        // Recovery pass: anything missing (skipped, or its block truncated mid-file and thus
        // unparseable) is retried one file at a time, the most truncation-resistant shape.
        foreach (var item in manifest.Where(m => !emitted.ContainsKey(m.Path)).ToArray())
            await GenerateBatchAsync([item], manifestText, contextDocs, userPrompt, emitted, cancellationToken);

        var missing = manifest.Where(m => !emitted.ContainsKey(m.Path)).Select(m => m.Path).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Code implementation incomplete after recovery pass — missing files: {string.Join(", ", missing)}. " +
                "Failing the stage rather than committing a partial implementation.");

        var output = new StringBuilder();
        foreach (var change in OrderEmitted(manifest, emitted))
        {
            output.AppendLine($"<file path=\"{change.Path}\">");
            output.AppendLine(change.Content);
            output.AppendLine("</file>");
            output.AppendLine();
        }

        return new AgentResult
        {
            AgentName      = Name,
            Status         = "Completed",
            Summary        = $"Code implementation generated for issue #{request.Context.IssueNumber} " +
                             $"({emitted.Count} files from a {manifest.Count}-file manifest).",
            OutputMarkdown = output.ToString()
        };
    }

    internal static bool IsRepairRequest(AgentContext ctx) =>
        (!string.IsNullOrWhiteSpace(GetMeta(ctx, "reopenFindings")) ||
         !string.IsNullOrWhiteSpace(GetMeta(ctx, "ciFindings")))
        && !string.IsNullOrWhiteSpace(GetMeta(ctx, "existingSource"));

    internal static IReadOnlyList<ManifestItem> ParseManifest(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return [];
        return ManifestItemRegex.Matches(responseText)
            .Select(m => new ManifestItem(m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()))
            .DistinctBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task GenerateBatchAsync(
        IReadOnlyList<ManifestItem> batch, string manifestText,
        Dictionary<string, string> contextDocs, string userPrompt,
        Dictionary<string, FileChange> emitted, CancellationToken cancellationToken)
    {
        var batchPrompt =
            $"""
            {userPrompt}

            The complete implementation plan (fixed — do not redesign it):
            {manifestText}

            Files already generated in earlier batches — shown here so the files you generate now
            stay consistent with them (matching class names, imports, routes, and DOM structure).
            Do NOT regenerate or re-output any of these:
            {DescribeEmitted(emitted)}

            Generate ONLY these files now, each one complete:
            {string.Join("\n", batch.Select(m => $"- {m.Path} — {m.Purpose}"))}
            """;

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeImplementation",
            SystemPrompt     = BatchSystemPrompt,
            UserPrompt       = batchPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = BatchMaxTokens
        }, cancellationToken);

        // Only complete blocks parse (the regex requires the closing tag), so a truncated
        // trailing file is naturally excluded and lands in the recovery pass.
        foreach (var change in CodeChangeParser.Parse(response.ResponseText))
            emitted[change.Path] = change;
    }

    // Renders the already-emitted files for a batch prompt. Each file's full CONTENT is included
    // while it fits the budget — so the files generated next (in a later batch, or alone in the
    // recovery pass) are authored against the real markup/structure of their siblings rather than an
    // imagined one. This is the fix for the cross-call class-name desync (sport121): styles.css and
    // app.js can now see the actual index.html they belong to. Past the budget, the remaining files
    // are listed by path only, so a large multi-file app keeps a bounded prompt.
    private static string DescribeEmitted(IReadOnlyDictionary<string, FileChange> emitted)
    {
        if (emitted.Count == 0) return "(none yet)";

        var sb = new StringBuilder();
        var remaining = EmittedContentBudgetChars;
        foreach (var (path, change) in emitted)
        {
            var block = $"<file path=\"{path}\">\n{change.Content}\n</file>";
            if (block.Length <= remaining)
            {
                sb.AppendLine(block);
                remaining -= block.Length;
            }
            else
            {
                sb.AppendLine($"- {path} (already generated; content omitted to bound prompt size)");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<AgentResult> RepairAsync(
        Dictionary<string, string> contextDocs, string userPrompt, int issueNumber,
        CancellationToken cancellationToken)
    {
        var modelRequest = new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeRepair",
            SystemPrompt     = RepairSystemPrompt,
            UserPrompt       = userPrompt +
                "\n\nApply the minimal fix for the findings document provided (Verification Findings " +
                "or CI Failure Findings) against the Existing Source. Output ONLY the corrected files.",
            ContextDocuments = contextDocs,
            MaxTokens        = 8000
        };

        var response = await _model.CompleteAsync(modelRequest, cancellationToken);
        if (!response.ResponseText.Contains("<file ", StringComparison.Ordinal))
        {
            response = await _model.CompleteAsync(modelRequest with
            {
                UserPrompt = $"{modelRequest.UserPrompt}\n\n{RetryPrompt}"
            }, cancellationToken);
        }

        var fileCount = CodeChangeParser.Parse(response.ResponseText).Count;
        return new AgentResult
        {
            AgentName      = Name,
            Status         = "Completed",
            Summary        = $"Repaired issue #{issueNumber}: minimal fix touching {fileCount} file(s) for the verification findings.",
            OutputMarkdown = response.ResponseText
        };
    }

    private async Task<AgentResult> SingleShotAsync(
        Dictionary<string, string> contextDocs, string userPrompt, int issueNumber,
        CancellationToken cancellationToken)
    {
        var modelRequest = new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeImplementation",
            SystemPrompt     = SingleShotSystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 8000
        };

        var response = await _model.CompleteAsync(modelRequest, cancellationToken);

        // Retry once if the model didn't produce any <file> blocks
        if (!response.ResponseText.Contains("<file ", StringComparison.Ordinal))
        {
            response = await _model.CompleteAsync(modelRequest with
            {
                UserPrompt = $"{userPrompt}\n\n{RetryPrompt}"
            }, cancellationToken);
        }

        return new AgentResult
        {
            AgentName      = Name,
            Status         = "Completed",
            Summary        = $"Code implementation generated for issue #{issueNumber}.",
            OutputMarkdown = response.ResponseText
        };
    }

    // Manifest order first (stable for review), then any extra files the model added.
    private static IEnumerable<FileChange> OrderEmitted(
        IReadOnlyList<ManifestItem> manifest, Dictionary<string, FileChange> emitted)
    {
        var inManifest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest)
        {
            if (emitted.TryGetValue(item.Path, out var change))
            {
                inManifest.Add(item.Path);
                yield return change;
            }
        }
        foreach (var (path, change) in emitted)
        {
            if (!inManifest.Contains(path))
                yield return change;
        }
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        // The Scaffold Contract is COMPOSED from the resolved capability axes (ScaffoldContract):
        // - stackProfile == "Static" → the self-contained Static contract (no React/Functions/Cosmos).
        // - else FullStack, assembled from auth (NeedsAuth) + persistence (needsDatabase) fragments, so an
        //   api-only app's contract omits Cosmos/RepositoryBase rather than being overridden after the fact.
        // Absent metadata defaults to today's behaviour (FullStack, auth on, database on).
        var isStatic    = string.Equals(GetMeta(ctx, "stackProfile"),   "Static", StringComparison.OrdinalIgnoreCase);
        var needsAuth   = !string.Equals(GetMeta(ctx, "needsAuth"),     "false",  StringComparison.OrdinalIgnoreCase);
        var hasDatabase = !string.Equals(GetMeta(ctx, "needsDatabase"), "false",  StringComparison.OrdinalIgnoreCase);
        docs[ScaffoldContractLabel] = ScaffoldContract.For(isStatic, needsAuth, hasDatabase);
        AddIfPresent(docs, ctx, "repoContext",       "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",       "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",    "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput",  "Architecture Review");
        // UX agent output (uxOutput) — accessibility review today, Design Direction once the UX agent is
        // elevated (static-design-quality.md §1). Threaded so the implementer BUILDS TO the visual
        // identity (palette/type/layout/motif) instead of producing correct-but-plain markup; without
        // this thread the Design Direction never reaches the coder.
        AddIfPresent(docs, ctx, "uxOutput",         "UX/UI Design Direction & Accessibility");
        AddIfPresent(docs, ctx, "implSpec",         "Implementation Specification");
        AddIfPresent(docs, ctx, "poReviewFeedback", "Product Owner Review Feedback (fix these issues)");
        AddIfPresent(docs, ctx, "existingSource",   "Existing Source (current code — fix in place, do not regenerate)");
        return docs;
    }

    private static string BuildUserPrompt(AgentContext ctx) =>
        $"""
        Repository: {ctx.Repository}
        Issue #{ctx.IssueNumber}: {GetMeta(ctx, "issueTitle")}

        {GetMeta(ctx, "issueBody")}
        """;

    private static void AddIfPresent(Dictionary<string, string> docs, AgentContext ctx, string key, string label)
    {
        var v = GetMeta(ctx, key);
        if (!string.IsNullOrWhiteSpace(v)) docs[label] = v;
    }

    private static string GetMeta(AgentContext ctx, string key) =>
        ctx.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
