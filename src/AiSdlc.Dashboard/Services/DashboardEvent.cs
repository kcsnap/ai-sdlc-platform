using AiSdlc.Shared;

namespace AiSdlc.Dashboard.Services;

// UI projection of AuditEvent. Includes a derived "API" label so rows can be grouped by entry point.
public sealed record DashboardEvent(
    DateTimeOffset TimestampUtc,
    string RunId,
    string Repository,
    int IssueNumber,
    int? PullRequestNumber,
    string Api,
    string ActorType,
    string ActorName,
    string Action,
    string Summary,
    string? Decision,
    string? RiskLevel,
    bool HasPromptArtefact,
    bool IsError,
    string? ErrorType,
    string? StackTrace,
    string? IssueTitle,
    string? IssueState,
    string? IssueStateReason,
    string? CommentUrl)
{
    public string Id => $"{RunId}|{TimestampUtc.UtcTicks:D20}|{ActorName}|{Action}";

    // Always-derivable URL pointing at the GitHub issue (or PR if one exists).
    public string IssueUrl => $"https://github.com/{Repository}/issues/{IssueNumber}";
    public string? PullRequestUrl => PullRequestNumber is int pr
        ? $"https://github.com/{Repository}/pull/{pr}"
        : null;

    // The most specific GitHub URL we can offer for this row: a direct comment link when
    // available, otherwise the PR (for PR events), otherwise the issue.
    public string PreferredGitHubUrl => CommentUrl
        ?? PullRequestUrl
        ?? IssueUrl;

    public static DashboardEvent FromAuditEvent(AuditEvent e)
    {
        var isAgent = string.Equals(e.ActorType, "Agent", StringComparison.OrdinalIgnoreCase);
        var isError = isAgent && string.Equals(e.Action, "Failed", StringComparison.OrdinalIgnoreCase);

        // Prompt/response blobs only exist for successful agent completions.
        var hasPrompt = isAgent && string.Equals(e.Action, "Completed", StringComparison.OrdinalIgnoreCase);

        string? errorType  = null;
        string? stackTrace = null;
        if (isError)
        {
            e.References.TryGetValue("exceptionType", out errorType);
            e.References.TryGetValue("stackTrace",    out stackTrace);
        }

        e.References.TryGetValue("commentUrl",       out var commentUrl);
        e.References.TryGetValue("issueState",       out var issueState);
        e.References.TryGetValue("issueStateReason", out var issueStateReason);

        return new DashboardEvent(
            TimestampUtc:      e.TimestampUtc,
            RunId:             e.RunId,
            Repository:        e.Repository,
            IssueNumber:       e.IssueNumber,
            PullRequestNumber: e.PullRequestNumber,
            Api:               DeriveApi(e),
            ActorType:         e.ActorType,
            ActorName:         e.ActorName,
            Action:            e.Action,
            Summary:           e.Summary,
            Decision:          e.Decision,
            RiskLevel:         e.RiskLevel,
            HasPromptArtefact: hasPrompt,
            IsError:           isError,
            ErrorType:         errorType,
            StackTrace:        stackTrace,
            IssueTitle:        ExtractIssueTitle(e),
            IssueState:        issueState,
            IssueStateReason:  issueStateReason,
            CommentUrl:        commentUrl);
    }

    private static string DeriveApi(AuditEvent e) => e.ActorType switch
    {
        "Webhook" => $"POST {e.ActorName}",
        "Agent"   => $"agent:{e.ActorName}",
        "Comment" => "github:comment",
        _         => e.ActorType
    };

    // The issue title is structured on the webhook event (References["issueTitle"]) going forward.
    // For audit events written before that change, fall back to parsing the Summary which the
    // webhook formats as "Issue #N {action}: {title}". Returns null for non-webhook events.
    private static string? ExtractIssueTitle(AuditEvent e)
    {
        if (!string.Equals(e.ActorType, "Webhook", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (e.References.TryGetValue("issueTitle", out var fromRefs) && !string.IsNullOrWhiteSpace(fromRefs))
        {
            return fromRefs;
        }

        // "Issue #28 opened: Add dark mode toggle" → "Add dark mode toggle"
        var marker = ": ";
        var prefix = $"Issue #{e.IssueNumber} ";
        if (e.Summary.StartsWith(prefix, StringComparison.Ordinal))
        {
            var idx = e.Summary.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
            if (idx > 0 && idx + marker.Length < e.Summary.Length)
            {
                return e.Summary[(idx + marker.Length)..].Trim();
            }
        }

        return null;
    }
}
