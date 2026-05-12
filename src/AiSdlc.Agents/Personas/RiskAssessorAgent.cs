using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class RiskAssessorAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Risk Assessor. You synthesise all prior agent reviews to produce a final risk decision for this change.

        Produce your assessment using these sections:

        ## Final Risk Level
        One of: LOW / MEDIUM / HIGH. This is your binding risk decision. One sentence justification.

        ## Risk Decision
        One of:
        - AUTO_MERGE_ELIGIBLE — low risk, all checks pass, proceed automatically
        - HUMAN_REVIEW_REQUIRED — medium or high risk, or blocking issues present
        - BLOCKED — critical finding that prevents progression entirely

        ## Risk Signals
        Bullet list of the signals that drove this decision (from security, compliance, devops, UX reviews, or deterministic rules).

        ## Blocking Issues
        Issues that MUST be resolved before this change can proceed. Number them. Omit if none.

        ## Conditions for Auto-Merge
        If the decision is HUMAN_REVIEW_REQUIRED, list what would need to change for the decision to become AUTO_MERGE_ELIGIBLE.

        ## Rationale
        2–3 sentences explaining the overall risk posture of this change.

        Be conservative: if any review raises a blocking issue, escalate to HUMAN_REVIEW_REQUIRED. Only mark AUTO_MERGE_ELIGIBLE if ALL reviews are clear, the change is low complexity, and no security or compliance concerns exist.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public RiskAssessorAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.RiskAssessor;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "RiskAssessment",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1500
        }, cancellationToken);

        var riskLevel  = ExtractRiskLevel(response.ResponseText);
        var decision   = ExtractDecision(response.ResponseText);
        var isBlocking = ExtractBlockingIssues(response.ResponseText);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Risk assessment completed. Level: {riskLevel}. Decision: {decision}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = decision,
            BlockingIssues   = isBlocking,
            ArtefactsCreated = ["risk-assessment.md"]
        };
    }

    private static string ExtractRiskLevel(string text)
    {
        if (text.Contains("HIGH",   StringComparison.OrdinalIgnoreCase)) return "HIGH";
        if (text.Contains("MEDIUM", StringComparison.OrdinalIgnoreCase)) return "MEDIUM";
        return "LOW";
    }

    private static string ExtractDecision(string text)
    {
        if (text.Contains("BLOCKED",                StringComparison.OrdinalIgnoreCase)) return "BLOCKED";
        if (text.Contains("HUMAN_REVIEW_REQUIRED",  StringComparison.OrdinalIgnoreCase)) return "HUMAN_REVIEW_REQUIRED";
        if (text.Contains("AUTO_MERGE_ELIGIBLE",    StringComparison.OrdinalIgnoreCase)) return "AUTO_MERGE_ELIGIBLE";
        return "HUMAN_REVIEW_REQUIRED";
    }

    private static List<string> ExtractBlockingIssues(string text)
    {
        if (text.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase))
            return ["Risk assessor raised a blocking issue — see risk assessment for details."];
        return [];
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",          "Repository Context");
        AddIfPresent(docs, ctx, "strategistOutput",     "Strategic Assessment");
        AddIfPresent(docs, ctx, "ownerBrief",           "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",        "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput",      "Architecture Review");
        AddIfPresent(docs, ctx, "securityOutput",       "Security & Privacy Review");
        AddIfPresent(docs, ctx, "uxOutput",             "UX & Accessibility Review");
        AddIfPresent(docs, ctx, "devopsOutput",         "DevOps Review");
        AddIfPresent(docs, ctx, "complianceOutput",     "Compliance & Legal Review");
        return docs;
    }

    private static string BuildUserPrompt(AgentContext ctx) =>
        $"""
        Repository: {ctx.Repository}
        Issue #{ctx.IssueNumber}: {GetMeta(ctx, "issueTitle")}

        Original request:
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
