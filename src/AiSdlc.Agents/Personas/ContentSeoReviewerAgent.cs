using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ContentSeoReviewerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Content & SEO Reviewer assessing the impact of a proposed change on content quality, brand voice, and search engine optimisation.

        Produce your review using these sections:

        ## Content Impact Summary
        One paragraph: does this change add, modify, or remove user-facing content? What is the impact?

        ## SEO Assessment
        Does this change affect: page titles, meta descriptions, heading hierarchy, URL structure, canonical tags, structured data, internal linking, or page performance? For each affected area, describe the impact and any required action.

        ## Content Quality
        Review any new or changed user-facing copy for: clarity, tone of voice, grammar, and alignment with brand guidelines. Flag specific issues.

        ## Required Changes
        Content or SEO changes that MUST be made before launch. Number them.

        ## Recommendations
        Non-blocking improvements. Number them.

        ## Open Questions
        Content or SEO questions needing resolution. Omit if none.

        If this change has no meaningful content or SEO impact, state that briefly.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public ContentSeoReviewerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.ContentSeoReviewer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "ContentSeoReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1500
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Content & SEO review completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Content review ready.",
            ArtefactsCreated = ["content-seo-review.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",   "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",    "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput", "Business Analysis");
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
