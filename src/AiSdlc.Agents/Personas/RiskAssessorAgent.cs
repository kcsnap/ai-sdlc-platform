using System.Text.RegularExpressions;
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

        Decision mapping — follow this exactly:
        - Final Risk Level = LOW  → AUTO_MERGE_ELIGIBLE, UNLESS a review explicitly raises a CRITICAL blocking issue
        - Final Risk Level = MEDIUM → HUMAN_REVIEW_REQUIRED
        - Final Risk Level = HIGH  → HUMAN_REVIEW_REQUIRED or BLOCKED

        What does NOT prevent AUTO_MERGE_ELIGIBLE on a LOW risk change:
        - Advisory suggestions, recommendations, or "nice to have" improvements
        - Open questions or items listed as "follow-up" or "out of scope"
        - Unvalidated assumptions that have no bearing on whether this specific change is safe to ship
        - Success metrics, audience definitions, or BA pain-point details (these are planning artefacts, not ship blockers)

        Blocking issues are ONLY: security vulnerabilities, data loss risks, compliance violations, or changes that would break existing functionality. If no such issues exist, LOW risk means AUTO_MERGE_ELIGIBLE — do not escalate.

        Open questions policy: questions raised by earlier agents and not explicitly flagged as blocking by ALL specialist reviewers are advisory. Do not treat unresolved advisory questions as blockers.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public RiskAssessorAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.RiskAssessor;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "RiskAssessment",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2500
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
        var section = ExtractSection(text, "Final Risk Level") ?? text;
        if (section.Contains("HIGH",   StringComparison.OrdinalIgnoreCase)) return "HIGH";
        if (section.Contains("MEDIUM", StringComparison.OrdinalIgnoreCase)) return "MEDIUM";
        return "LOW";
    }

    private static string ExtractDecision(string text)
    {
        var section = ExtractSection(text, "Risk Decision") ?? string.Empty;
        foreach (var line in section.Split('\n'))
        {
            var trimmed = line.Trim().TrimStart('-', '*', ' ').Trim();
            if (StartsWithKeyword(trimmed, "BLOCKED"))              return "BLOCKED";
            if (StartsWithKeyword(trimmed, "HUMAN_REVIEW_REQUIRED")) return "HUMAN_REVIEW_REQUIRED";
            if (StartsWithKeyword(trimmed, "AUTO_MERGE_ELIGIBLE"))   return "AUTO_MERGE_ELIGIBLE";
        }
        return "HUMAN_REVIEW_REQUIRED";
    }

    private static bool StartsWithKeyword(string line, string keyword) =>
        line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) &&
        (line.Length == keyword.Length || !char.IsLetterOrDigit(line[keyword.Length]));

    private static List<string> ExtractBlockingIssues(string text)
    {
        if (ExtractDecision(text) == "BLOCKED")
            return ["Risk assessor raised a blocking issue — see risk assessment for details."];
        return [];
    }

    private static string? ExtractSection(string text, string heading)
    {
        var match = Regex.Match(text,
            $@"##\s*{Regex.Escape(heading)}\s*\n(.*?)(?=\n##|\z)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
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
        AddIfPresent(docs, ctx, "contentOutput",        "Content & SEO Review");
        AddIfPresent(docs, ctx, "analyticsOutput",      "Data & Analytics Review");
        AddIfPresent(docs, ctx, "testPlan",             "Test Plan");
        AddIfPresent(docs, ctx, "implSpec",             "Implementation Spec");
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
