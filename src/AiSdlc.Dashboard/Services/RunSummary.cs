namespace AiSdlc.Dashboard.Services;

public enum RunStatus
{
    Unknown,
    Pending,      // webhook received but no agent activity yet
    Running,      // agents are executing or finished but no terminal release
    Failed,       // an agent's latest outcome is Failed, or orchestrator wrote Workflow Failed
    Stopped,      // orchestrator exited early as Stopped (timeout, blocked, no-op implementer, etc.)
    Released,     // ReleaseManager completed
}

// One row in the /issues list — represents a single workflow run.
public sealed record RunSummary(
    string RunId,
    string Repository,
    int IssueNumber,
    int? PullRequestNumber,
    string? IssueTitle,
    string? IssueState,        // "open" or "closed" from GitHub, when available
    string? IssueStateReason,  // "completed" | "not_planned" | "duplicate" | "reopened" | null
    DateTimeOffset FirstActivityUtc,
    DateTimeOffset LatestActivityUtc,
    int EventCount,
    int AgentCount,
    int FailedEventCount,
    int RetryCount,
    RunStatus Status,
    string LatestActor,
    string LatestAction,
    string LatestSummary)
{
    public string IssueUrl => $"https://github.com/{Repository}/issues/{IssueNumber}";
    public string? PullRequestUrl => PullRequestNumber is int pr
        ? $"https://github.com/{Repository}/pull/{pr}"
        : null;

    // Display-friendly state label e.g. "Open", "Closed", "Closed as not planned".
    // Returns null when we have no signal yet (don't assume "Open" since the issue could be closed).
    public string? IssueStateLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IssueState)) return null;

            var state = IssueState.Trim().ToLowerInvariant();
            if (state != "closed")
            {
                return char.ToUpperInvariant(state[0]) + state[1..];
            }

            return IssueStateReason?.Trim().ToLowerInvariant() switch
            {
                "completed"   => "Closed as completed",
                "not_planned" => "Closed as not planned",
                "duplicate"   => "Closed as duplicate",
                "reopened"    => "Reopened",
                _             => "Closed"
            };
        }
    }

    // CSS modifier slug for the GitHub-state chip.
    public string IssueStateSlug => IssueStateLabel switch
    {
        null                            => "none",
        "Open"                          => "open",
        "Reopened"                      => "open",
        "Closed as not planned"         => "closed-not-planned",
        "Closed as duplicate"           => "closed-not-planned",
        _ when IssueStateLabel!.StartsWith("Closed", StringComparison.Ordinal) => "closed",
        _                               => "none"
    };
}
