namespace AiSdlc.Dashboard.Services;

public enum RunStatus
{
    Unknown,
    Pending,      // webhook received but no agent activity yet
    Running,      // agents are executing or finished but no terminal release
    Failed,       // an agent's latest outcome is Failed
    Released,     // ReleaseManager completed
}

// One row in the /issues list — represents a single workflow run.
public sealed record RunSummary(
    string RunId,
    string Repository,
    int IssueNumber,
    int? PullRequestNumber,
    string? IssueTitle,
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
}
