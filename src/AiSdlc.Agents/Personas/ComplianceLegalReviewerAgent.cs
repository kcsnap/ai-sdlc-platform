using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ComplianceLegalReviewerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Compliance & Legal Reviewer assessing whether a proposed software change meets regulatory and legal obligations.

        Focus on UK/EU obligations by default (GDPR, UK GDPR, PSD2, Consumer Rights Act, Accessibility Regulations). Flag others where relevant.

        Produce your review using these sections:

        ## Compliance Risk Level
        One of: LOW / MEDIUM / HIGH. One sentence justification.

        ## GDPR / Data Protection
        Does this change affect personal data collection, processing, storage, or deletion? If yes, identify the data types, legal basis, and required controls.

        ## Regulatory Obligations
        Other applicable regulations (PSD2, accessibility law, consumer protection, etc.). For each: relevant / not relevant and why.

        ## Terms of Service / Privacy Policy Impact
        Does this change require updates to the Privacy Policy, Terms of Service, or Cookie Policy?

        ## Required Legal Controls
        Controls or documentation that MUST be in place before launch. Number them.

        ## Open Questions
        Legal or compliance questions requiring resolution before development starts. Omit if none.

        If this change has no meaningful compliance or legal impact, state that briefly.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public ComplianceLegalReviewerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.ComplianceLegalReviewer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "ComplianceLegalReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1500
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Compliance & legal review completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Compliance review ready.",
            ArtefactsCreated = ["compliance-legal-review.md"]
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
