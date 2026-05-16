using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class QaTestEngineerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a QA / Test Engineer producing a test plan for a proposed change.

        Produce the test plan using these sections:

        ## Test Strategy
        One paragraph: what testing approach is appropriate for this change (unit, integration, E2E, manual, accessibility, performance) and why.

        ## Unit Tests Required
        Specific unit tests that must be written. Each entry should name the function/component under test and describe the test scenario.

        ## Integration Tests Required
        Integration test scenarios covering component interactions, API contracts, and data flows.

        ## E2E / Manual Tests Required
        User-facing scenarios that must be validated. Written as step-by-step flows.

        ## Regression Risk Areas
        Existing functionality most likely to be broken by this change. List areas to regression-test.

        ## Coverage Expectations
        Specific coverage requirements or areas where coverage must not drop.

        ## Definition of Done — Testing
        A numbered checklist. The PR must not merge until all items are checked off.

        ## Answers to Open Questions
        If any context documents contain an "## Open Questions" section raised by another agent, answer every question related to test coverage, validation criteria, quality requirements, or testability. Omit this section if there are no open questions to answer.

        Be specific to the stack and the change. Reference actual component names and file paths where known from the context.
        Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public QaTestEngineerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.QaTestEngineer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "TestPlan",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2000
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Test plan produced for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Test plan ready.",
            ArtefactsCreated = ["test-plan.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",      "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",       "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",    "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput",  "Architecture Review");
        AddIfPresent(docs, ctx, "securityOutput",   "Security & Privacy Review");
        AddIfPresent(docs, ctx, "uxOutput",         "UX & Accessibility Review");
        AddIfPresent(docs, ctx, "devopsOutput",     "DevOps & Platform Review");
        AddIfPresent(docs, ctx, "contentOutput",    "Content & SEO Review");
        AddIfPresent(docs, ctx, "complianceOutput", "Compliance & Legal Review");
        AddIfPresent(docs, ctx, "analyticsOutput",  "Data & Analytics Review");
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
