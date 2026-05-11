using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ProductStrategistAgent : IAgent
{
    private const string SystemPrompt = """
        You are an experienced Product Strategist reviewing a GitHub issue for a software product.

        Assess the following and respond in clean GitHub-flavoured markdown using these sections:

        ## Strategic Assessment
        One sentence verdict on whether to proceed and why.

        ## Business Value
        Who benefits and how (2–3 bullets max).

        ## Feasibility & Risks
        Key technical or delivery risks (2–3 bullets max).

        ## Recommended Scope
        The smallest delivery slice that provides real user value.

        ## Open Questions
        Critical unknowns. Omit this section entirely if there are none.

        Be direct and concise. Do not pad. If the issue lacks detail, note it briefly.
        """;

    private readonly IModelProvider _model;

    public ProductStrategistAgent(IModelProvider model)
    {
        _model = model;
    }

    public string Name => AgentNames.ProductStrategist;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issueTitle  = GetMeta(request.Context, "issueTitle");
        var issueBody   = GetMeta(request.Context, "issueBody");
        var issueAuthor = GetMeta(request.Context, "issueAuthor");

        var userPrompt = $"""
            Repository: {request.Context.Repository}
            Issue #{request.Context.IssueNumber} by @{issueAuthor}: {issueTitle}

            {issueBody}
            """;

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName    = Name,
            TaskType     = "StrategicAssessment",
            SystemPrompt = SystemPrompt,
            UserPrompt   = userPrompt,
            MaxTokens    = 1024
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Strategic assessment completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Proceed to product brief.",
            ArtefactsCreated = ["strategy-brief.md"]
        };
    }

    private static string GetMeta(AgentContext context, string key) =>
        context.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
