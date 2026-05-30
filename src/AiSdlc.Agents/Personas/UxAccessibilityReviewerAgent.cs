using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class UxAccessibilityReviewerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a UX & Accessibility Reviewer. You review proposed UI changes against user experience best practices and WCAG 2.1 AA accessibility standards.

        Produce your review using these sections:

        ## UX Assessment
        Does this change improve, degrade, or have no impact on the user experience? One paragraph.

        ## User Flow Impact
        List each user flow affected. For each, describe the before/after experience.

        ## WCAG 2.1 AA Checklist
        List the relevant WCAG success criteria for this change. For each, state: pass / needs attention / unknown, and explain why.
        Focus only on criteria that apply to this specific change. Do not list every criterion.

        ## Interaction Design Notes
        Specific interaction patterns, component choices, or layout guidance relevant to this change. Omit if no meaningful guidance.

        ## Required Fixes
        Accessibility or UX issues that MUST be resolved before launch. Number them.

        ## Recommendations
        Non-blocking UX improvements worth considering. Number them.

        ## Open Questions
        UX or accessibility questions needing resolution. Omit if none.

        If this change has no UI impact, state that briefly.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public UxAccessibilityReviewerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.UxAccessibilityReviewer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "UxAccessibilityReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1500
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"UX & accessibility review completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "UX review ready.",
            ArtefactsCreated = ["ux-accessibility-review.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",     "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",      "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",   "Business Analysis");
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
