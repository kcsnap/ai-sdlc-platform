using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ReleaseManagerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Release Manager. You produce release documentation for a proposed change that has passed all reviews and is ready for implementation.

        Produce your output using these sections:

        ## Release Summary
        One paragraph suitable for a public changelog or release notes entry. Plain English, no jargon.

        ## What's Changing
        Bullet list of user-visible changes. Written from the user's perspective.

        ## What's Not Changing
        Bullet list confirming what is explicitly out of scope. This sets expectations.

        ## Deployment Approach
        How this change should be deployed: standard release / feature flag / phased rollout / maintenance window. Justify your recommendation.

        ## Rollback Plan
        Step-by-step instructions to roll back this change if issues are detected post-deployment. Include: trigger conditions, rollback steps, verification steps.

        ## Post-Deployment Checks
        Numbered list of checks that must pass within the first 30 minutes after deployment.

        ## Communication
        Who needs to be notified before, during, and after the release? What should they be told?

        Be specific and actionable. This document is used by the person performing the deployment.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public ReleaseManagerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.ReleaseManager;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "ReleaseNotes",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2000
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Release documentation produced for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Release documentation ready.",
            ArtefactsCreated = ["release-notes.md", "rollback-plan.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",       "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",        "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",     "Business Analysis");
        AddIfPresent(docs, ctx, "riskAssessment",    "Risk Assessment");
        AddIfPresent(docs, ctx, "implSpec",          "Implementation Specification");
        AddIfPresent(docs, ctx, "testPlan",          "Test Plan");
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
