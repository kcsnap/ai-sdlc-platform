using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents;

public sealed class BusinessAnalystAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Business Analyst producing a developer handoff document for an approved product brief.

        Produce a structured handoff using these sections:

        ## Change Summary
        One paragraph: what is changing, why, and for whom.

        ## Impacted Areas
        List product areas, user flows, and system components affected.

        ## Acceptance Criteria
        Precise, testable criteria. Number them.

        ## Constraints & Dependencies
        Technical, legal, or timeline constraints and dependencies on other work. Omit this section entirely if there are none.

        ## Developer Guidance
        Specific patterns to follow, pitfalls to avoid, testing expectations.

        ## Open Questions
        Anything that must be resolved before or during development. Omit this section entirely if there are none.

        Be precise and actionable. A developer should be able to start work from this document alone.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public BusinessAnalystAgent(IModelProvider model)
    {
        _model = model;
    }

    public string Name => AgentNames.BusinessAnalyst;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issueTitle      = GetMeta(request.Context, "issueTitle");
        var issueBody       = GetMeta(request.Context, "issueBody");
        var strategistOutput = GetMeta(request.Context, "strategistOutput");
        var ownerBrief      = GetMeta(request.Context, "ownerBrief");

        var contextDocs = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(strategistOutput))
            contextDocs["Strategic Assessment"] = strategistOutput;
        if (!string.IsNullOrWhiteSpace(ownerBrief))
            contextDocs["Approved Product Brief"] = ownerBrief;

        var userPrompt = $"""
            Repository: {request.Context.Repository}
            Issue #{request.Context.IssueNumber}: {issueTitle}

            Original request:
            {issueBody}
            """;

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "BusinessAnalysis",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2000
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Business analysis produced for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Developer handoff ready.",
            ArtefactsCreated = ["business-analysis.md"]
        };
    }

    private static string GetMeta(AgentContext context, string key) =>
        context.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
