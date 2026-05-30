using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class SecurityPrivacyReviewerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Security & Privacy Reviewer performing a pre-implementation review.

        Review the proposed change against OWASP Top 10 and GDPR/data-privacy obligations.

        Produce your review using these sections:

        ## Security Risk Level
        One of: LOW / MEDIUM / HIGH. One sentence justification.

        ## OWASP Top 10 Assessment
        List only the OWASP categories that are relevant to this change. For each, state: relevant / not relevant and why. Skip categories with no relevance.

        ## Data Privacy (GDPR)
        Does this change process, store, transmit, or delete personal data? If yes, list the data types and the applicable GDPR obligations (lawful basis, retention, data subject rights).

        ## Authentication & Authorisation Impact
        Describe any changes to auth flows, session handling, permissions, or access control.

        ## Required Security Controls
        Specific controls that MUST be implemented before this change goes live. Number them.

        ## Recommended Controls
        Controls that SHOULD be implemented but are not blocking. Number them.

        ## Open Questions
        Security or privacy questions that must be resolved before implementation. Omit if none.

        If the change has no meaningful security or privacy impact, state that briefly and omit empty sections.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public SecurityPrivacyReviewerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.SecurityPrivacyReviewer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "SecurityPrivacyReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2000
        }, cancellationToken);

        var riskLevel = ExtractRiskLevel(response.ResponseText);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Security & privacy review completed. Risk level: {riskLevel}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = riskLevel,
            ArtefactsCreated = ["security-privacy-review.md"]
        };
    }

    private static string ExtractRiskLevel(string text)
    {
        if (text.Contains("HIGH",   StringComparison.OrdinalIgnoreCase)) return "HIGH";
        if (text.Contains("MEDIUM", StringComparison.OrdinalIgnoreCase)) return "MEDIUM";
        return "LOW";
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",       "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",        "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",     "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput",   "Architecture Review");
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
