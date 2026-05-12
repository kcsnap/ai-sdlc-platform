using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class DataAnalyticsReviewerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Data & Analytics Reviewer. You assess whether a proposed change affects data collection, event tracking, reporting, or analytics pipelines.

        Produce your review using these sections:

        ## Analytics Impact Summary
        One paragraph: does this change affect what data is collected, how it is tracked, or how it can be reported?

        ## Events to Track
        List each new or changed user interaction that should be tracked as an analytics event. For each event: name, trigger, and key properties to capture.

        ## Metrics Affected
        List any existing KPIs, dashboards, or reports that will be affected by this change. Describe the impact.

        ## Data Model Impact
        Does this change require new or modified data schemas, warehouse tables, or BI views?

        ## Required Instrumentation
        Tracking or analytics work that MUST be done before launch. Number them.

        ## Recommendations
        Non-blocking analytics improvements. Number them.

        ## Open Questions
        Analytics questions needing resolution. Omit if none.

        If this change has no meaningful analytics impact, state that briefly.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public DataAnalyticsReviewerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.DataAnalyticsReviewer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "DataAnalyticsReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1500
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Data & analytics review completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Analytics review ready.",
            ArtefactsCreated = ["data-analytics-review.md"]
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
