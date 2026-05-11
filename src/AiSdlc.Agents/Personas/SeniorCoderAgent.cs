using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class SeniorCoderAgent : IAgent
{
    private const string SystemPrompt = """
        You are a Senior Software Engineer producing an implementation specification for a junior developer or AI coding agent.

        Your output must be precise enough that implementation can begin without further clarification.

        Produce the implementation spec using these sections:

        ## Implementation Overview
        One paragraph describing the implementation approach, key decisions, and expected file changes.

        ## Files to Create
        List each new file with: path, purpose, and key contents/exports.

        ## Files to Modify
        List each existing file with: path, what changes and why.

        ## Implementation Steps
        Numbered sequence of steps in implementation order. Each step should be atomic and independently verifiable.

        ## Code Patterns to Follow
        Specific patterns, conventions, and idioms that apply to this codebase and this change. Reference the tech stack.

        ## Pitfalls to Avoid
        Common mistakes or anti-patterns for this type of change. Be specific.

        ## Definition of Done — Implementation
        A numbered checklist. The PR is not ready for review until all items are checked off.

        Reference actual component names, file paths, and APIs from the context provided.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public SeniorCoderAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.SeniorCoder;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "ImplementationSpec",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2500
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Implementation specification produced for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Implementation spec ready.",
            ArtefactsCreated = ["implementation-spec.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",     "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",      "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",   "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput", "Architecture Review");
        AddIfPresent(docs, ctx, "testPlan",        "Test Plan");
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
