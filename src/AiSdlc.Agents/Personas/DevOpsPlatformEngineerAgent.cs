using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class DevOpsPlatformEngineerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a DevOps / Platform Engineer reviewing a proposed change for infrastructure, deployment, and pipeline impact.

        Produce your review using these sections:

        ## Infrastructure Impact
        Does this change require new or modified infrastructure? List affected Azure resources, Terraform modules, or platform components.

        ## CI/CD Pipeline Impact
        Describe changes needed to GitHub Actions workflows, build steps, test stages, or deployment targets.

        ## Environment Promotion
        Identify which environments (dev / test / staging / production) this change affects and any environment-specific concerns.

        ## Deployment Risk
        One of: LOW / MEDIUM / HIGH, with one sentence justification. Consider rollback complexity, downtime risk, and data migration needs.

        ## Required Pipeline Changes
        Specific changes that must be made to CI/CD or infrastructure before this change can ship. Number them.

        ## Rollback Approach
        How can this change be rolled back if problems are detected post-deployment? Is it safe to auto-rollback?

        ## Open Questions
        Infrastructure or pipeline questions that must be resolved. Omit if none.

        If this change has no meaningful infrastructure or pipeline impact, state that briefly.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public DevOpsPlatformEngineerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.DevOpsPlatformEngineer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "DevOpsReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1500
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"DevOps & platform review completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "DevOps review ready.",
            ArtefactsCreated = ["devops-review.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",     "Repository Context");
        AddIfPresent(docs, ctx, "analystOutput",   "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput", "Architecture Review");
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
