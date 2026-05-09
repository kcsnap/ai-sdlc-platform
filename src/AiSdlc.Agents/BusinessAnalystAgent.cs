using AiSdlc.Shared;

namespace AiSdlc.Agents;

public sealed class BusinessAnalystAgent : IAgent
{
    public string Name => AgentNames.BusinessAnalyst;

    public Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var specMarkdown = GetMetadataValue(request.Context, "specMarkdown");
        var title = GetMetadataValue(request.Context, "specTitle");
        var existingProductContext = GetMetadataValue(request.Context, "existingProductContext");

        var parsedSpec = BusinessAnalystSpecParser.Parse(title, specMarkdown, existingProductContext);
        var followUpQuestions = BuildFollowUpQuestions(parsedSpec);
        var blockingIssues = BuildBlockingIssues(parsedSpec);
        var outputMarkdown = BusinessAnalystMarkdownRenderer.Render(parsedSpec, followUpQuestions);

        var status = blockingIssues.Count == 0 ? "Completed" : "NeedsClarification";

        return Task.FromResult(new AgentResult
        {
            AgentName = Name,
            Status = status,
            Summary = BuildSummary(parsedSpec, blockingIssues.Count == 0),
            OutputMarkdown = outputMarkdown,
            Decision = blockingIssues.Count == 0
                ? "Developer-ready analysis generated."
                : "More detail is required before a clean developer handoff.",
            ArtefactsCreated = new List<string> { "business-analyst-review.md" },
            FollowUpQuestions = followUpQuestions,
            BlockingIssues = blockingIssues
        });
    }

    private static string BuildSummary(BusinessAnalystChangeRequest parsedSpec, bool isReady) =>
        isReady
            ? $"Reviewed spec '{Fallback(parsedSpec.Title)}' and produced a developer-ready BA handoff."
            : $"Reviewed spec '{Fallback(parsedSpec.Title)}' and identified clarification gaps before developer handoff.";

    private static List<string> BuildBlockingIssues(BusinessAnalystChangeRequest parsedSpec)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(parsedSpec.ChangeRequest))
        {
            issues.Add("The requested change is not described.");
        }

        if (string.IsNullOrWhiteSpace(parsedSpec.BusinessNeed))
        {
            issues.Add("The business reason for the change is missing.");
        }

        if (string.IsNullOrWhiteSpace(parsedSpec.TargetUser))
        {
            issues.Add("The target user or customer is not identified.");
        }

        return issues;
    }

    private static List<string> BuildFollowUpQuestions(BusinessAnalystChangeRequest parsedSpec)
    {
        var questions = new List<string>();

        if (string.IsNullOrWhiteSpace(parsedSpec.TargetUser))
        {
            questions.Add("Who is the primary user or customer for this change?");
        }

        if (string.IsNullOrWhiteSpace(parsedSpec.DefinitionOfDone))
        {
            questions.Add("What acceptance criteria or definition of done should the developer satisfy?");
        }

        if (string.IsNullOrWhiteSpace(parsedSpec.ExistingProductContext))
        {
            questions.Add("What is the relevant current product behaviour or page flow that this change should be compared against?");
        }

        return questions;
    }

    private static string GetMetadataValue(AgentContext context, string key) =>
        context.Metadata.TryGetValue(key, out var value)
            ? Convert.ToString(value) ?? string.Empty
            : string.Empty;

    private static string Fallback(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Untitled change request" : title.Trim();
}
