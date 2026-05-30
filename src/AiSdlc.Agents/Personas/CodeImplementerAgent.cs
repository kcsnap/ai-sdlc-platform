using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class CodeImplementerAgent : IAgent
{
    private const string SystemPrompt = """
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

    private readonly IModelProvider _model;

    public CodeImplementerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.CodeImplementer;

    private const string RetryPrompt =
        "Your previous response contained no `<file path=\"...\">` blocks. " +
        "You MUST wrap every file in `<file path=\"...\">` tags. " +
        "Output ONLY file blocks — nothing else.";

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var modelRequest = new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeImplementation",
            SystemPrompt     = SystemPrompt,
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
            Summary        = $"Code implementation generated for issue #{request.Context.IssueNumber}.",
            OutputMarkdown = response.ResponseText
        };
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
