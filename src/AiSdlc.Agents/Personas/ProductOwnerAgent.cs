using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ProductOwnerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Product Owner writing a refined product brief for a GitHub issue.

        Transform the input into a structured brief using these exact sections:

        ## Summary
        One sentence description of the change.

        ## User Story
        As a [user], I want [capability], so that [benefit].

        ## Acceptance Criteria
        - [ ] Criterion 1
        - [ ] Criterion 2
        (add as many as needed)

        ## Out of Scope
        What this change explicitly does NOT include.

        ## Open Questions
        Ambiguities the submitter should clarify before development starts. Omit this section entirely if there are none.

        ---
        Reply `/approve-brief` to proceed or `/request-changes` with your feedback.

        Write clean GitHub-flavoured markdown. If the issue lacks detail, make reasonable assumptions and flag them in Open Questions.
        """;

    private readonly IModelProvider _model;

    public ProductOwnerAgent(IModelProvider model)
    {
        _model = model;
    }

    public string Name => AgentNames.ProductOwner;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issueTitle      = GetMeta(request.Context, "issueTitle");
        var issueBody       = GetMeta(request.Context, "issueBody");
        var strategistOutput = GetMeta(request.Context, "strategistOutput");
        var repoContext      = GetMeta(request.Context, "repoContext");

        var contextDocs = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(repoContext))
            contextDocs["Repository Context"] = repoContext;
        if (!string.IsNullOrWhiteSpace(strategistOutput))
            contextDocs["Strategic Assessment"] = strategistOutput;
        AgentContextDocuments.AddStandard(contextDocs, request.Context);

        var userPrompt = $"""
            Repository: {request.Context.Repository}
            Issue #{request.Context.IssueNumber}: {issueTitle}

            {issueBody}
            """;

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "ProductBrief",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1500
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Product brief written for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Brief ready for human review.",
            ArtefactsCreated = ["product-brief.md"]
        };
    }

    private static string GetMeta(AgentContext context, string key) =>
        context.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
