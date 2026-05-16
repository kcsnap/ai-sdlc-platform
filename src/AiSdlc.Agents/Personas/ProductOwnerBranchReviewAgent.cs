using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class ProductOwnerBranchReviewAgent : IAgent
{
    private const string SystemPrompt = """
        You are the Product Owner reviewing files committed to an implementation branch.

        Your job:
        1. Verify every file is complete and not truncated — no missing sections, no cut-off content.
        2. Verify the content matches the approved brief and acceptance criteria.
        3. For documentation files (README, etc.), confirm the markdown is valid and fully present.

        For each issue you find, classify it as one of:
          CRITICAL   — would cause real harm if shipped: broken instructions, security exposure,
                       misleading content, corrupted/truncated output, missing mandatory section
                       that the implementer had all context to produce.
          ADVISORY   — style preference, optional enhancement, open question, best-practice
                       suggestion, or anything the implementer couldn't resolve from the brief.

        Decision rules:
          • Any CRITICAL issue → respond CHANGES_REQUIRED on the first line.
          • All issues are ADVISORY (or no issues) → respond APPROVED on the first line.

        Format:
          APPROVED
          [ADVISORY] <description>   (list any advisory notes, or omit if none)

          — or —

          CHANGES_REQUIRED
          [CRITICAL] <description>
          [ADVISORY] <description>   (include advisory items too so the fixer has full context)

        Keep the review concise.
        """;

    private readonly IModelProvider _model;

    public ProductOwnerBranchReviewAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.ProductOwnerBranchReview;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var branchName = GetMeta(request.Context, "branchName");

        var contextDocs = new Dictionary<string, string>();
        AddIfPresent(contextDocs, request.Context, "ownerBrief",    "Approved Product Brief");
        AddIfPresent(contextDocs, request.Context, "analystOutput", "Business Analysis");
        AddIfPresent(contextDocs, request.Context, "branchContent", "Committed Files");

        var userPrompt = $"""
            Repository: {request.Context.Repository}
            Branch: {branchName}
            Issue #{request.Context.IssueNumber}

            Review the committed files. Start your response with APPROVED or CHANGES_REQUIRED.
            """;

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "BranchContentReview",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 1000
        }, cancellationToken);

        var decision = response.ResponseText.TrimStart().StartsWith("CHANGES_REQUIRED", StringComparison.Ordinal)
            ? "CHANGES_REQUIRED"
            : "APPROVED";

        return new AgentResult
        {
            AgentName      = Name,
            Status         = "Completed",
            Summary        = $"Branch content review for issue #{request.Context.IssueNumber}: {decision}.",
            OutputMarkdown = response.ResponseText,
            Decision       = decision
        };
    }

    private static void AddIfPresent(Dictionary<string, string> docs, AgentContext ctx, string key, string label)
    {
        var v = GetMeta(ctx, key);
        if (!string.IsNullOrWhiteSpace(v)) docs[label] = v;
    }

    private static string GetMeta(AgentContext ctx, string key) =>
        ctx.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
