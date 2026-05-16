using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ArchitectAgent : IAgent
{
    private const string SystemPrompt = """
        You are a software Architect reviewing a proposed change for an existing application.

        Produce an architecture review using these sections:

        ## Technical Approach
        The recommended implementation approach in 2–3 sentences. Be specific about patterns, layers, and components.

        ## Component Impact
        List each component, service, or layer that will be modified or introduced. For each, describe the nature of the change (new / modified / deleted).

        ## Recommended Patterns
        Specific design patterns, conventions, or architectural guidelines that apply. Reference existing patterns in the codebase where known.

        ## Risks & Trade-offs
        Technical risks introduced by this change. Include coupling concerns, performance, scalability, or maintainability issues. 2–4 bullets max.

        ## ADR Required
        State YES or NO. If YES, briefly describe the architectural decision that needs recording.

        ## Open Questions
        Unresolved technical questions that must be answered before or during implementation. Omit if none.

        ## Answers to Open Questions
        If any context documents contain an "## Open Questions" section raised by another agent, answer every question that requires an architectural or technical decision — exact versions, dependency rationale, patterns to adopt, infrastructure choices. Omit this section if there are no open questions to answer.

        Be precise and reference the actual stack (frameworks, languages, patterns) from the context you are given.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public ArchitectAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.Architect;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "ArchitectureReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2000
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Architecture review completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Architecture review ready.",
            ArtefactsCreated = ["architecture-review.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",       "Repository Context");
        AddIfPresent(docs, ctx, "strategistOutput",  "Strategic Assessment");
        AddIfPresent(docs, ctx, "ownerBrief",        "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",     "Business Analysis");
        return docs;
    }

    private static string BuildUserPrompt(AgentContext ctx)
    {
        var title = GetMeta(ctx, "issueTitle");
        var body  = GetMeta(ctx, "issueBody");
        return $"""
            Repository: {ctx.Repository}
            Issue #{ctx.IssueNumber}: {title}

            {body}
            """;
    }

    private static void AddIfPresent(Dictionary<string, string> docs, AgentContext ctx, string key, string label)
    {
        var v = GetMeta(ctx, key);
        if (!string.IsNullOrWhiteSpace(v)) docs[label] = v;
    }

    private static string GetMeta(AgentContext ctx, string key) =>
        ctx.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
