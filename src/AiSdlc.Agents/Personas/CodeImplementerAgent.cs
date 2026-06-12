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

    private const string ManifestSystemPrompt = """
        You are the Code Implementer planning stage in an AI-driven SDLC pipeline.

        Plan the COMPLETE set of files needed to implement the feature based on the brief,
        business analysis, architecture review, and implementation specification provided.

        Rules:
        - Output ONLY a manifest block — no prose, no explanation, nothing outside it:

          <manifest>
          <item path="relative/path/from/repo/root">one-line purpose of the file</item>
          </manifest>

        - List every file required for a complete, runnable implementation — if an entrypoint
          or import will reference a file, that file MUST be in the manifest.
        - Keep the design modular: prefer many focused files over few large ones, and keep the
          total under 40 files.
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

        The application already exists and was released, but failed downstream verification.
        You are given the CURRENT source code and the verification findings (often exact
        compiler output). Your job is a surgical fix, not a rewrite.

        Rules:
        - Fix ONLY what the findings implicate. Do not redesign, restructure, rename, or
          "improve" anything else — unchanged files must not be touched.
        - Output ONLY the files that need to change, each as a complete file using EXACTLY:

          <file path="relative/path/from/repo/root">
          (complete fixed file content)
          </file>

        - Each output file must be the COMPLETE corrected file, based on the existing
          content shown to you — never a fragment or diff.
        - Output nothing outside the file blocks.
        - The literal text </file> must never appear inside file content.
        """;

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

        // Repair mode: the app exists and failed verification — iterate on the released
        // code with the findings, never regenerate. Regeneration cannot converge: each
        // rewrite introduces fresh defects in different files (#92).
        if (!string.IsNullOrWhiteSpace(GetMeta(request.Context, "reopenFindings")) &&
            !string.IsNullOrWhiteSpace(GetMeta(request.Context, "existingSource")))
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
        var alreadyGenerated = emitted.Count == 0
            ? "(none yet)"
            : string.Join("\n", emitted.Keys.Select(p => $"- {p}"));

        var batchPrompt =
            $"""
            {userPrompt}

            The complete implementation plan (fixed — do not redesign it):
            {manifestText}

            Files already generated in earlier batches (do not regenerate):
            {alreadyGenerated}

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
            MaxTokens        = 8000
        }, cancellationToken);

        // Only complete blocks parse (the regex requires the closing tag), so a truncated
        // trailing file is naturally excluded and lands in the recovery pass.
        foreach (var change in CodeChangeParser.Parse(response.ResponseText))
            emitted[change.Path] = change;
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
                "\n\nApply the minimal fix for the Verification Findings against the Existing Source. " +
                "Output ONLY the corrected files.",
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
        AddIfPresent(docs, ctx, "repoContext",       "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",       "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",    "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput",  "Architecture Review");
        AddIfPresent(docs, ctx, "implSpec",         "Implementation Specification");
        AddIfPresent(docs, ctx, "poReviewFeedback", "Product Owner Review Feedback (fix these issues)");
        AddIfPresent(docs, ctx, "existingSource",   "Existing Source (current released code — fix in place, do not regenerate)");
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
