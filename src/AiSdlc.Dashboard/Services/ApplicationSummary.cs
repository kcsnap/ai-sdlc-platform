namespace AiSdlc.Dashboard.Services;

// One row on the /applications page. An "application" is a single GitHub repository that the
// AI SDLC platform is wired to (e.g. "kcsnap/launchcart"). Aggregates across all its workflow runs.
public sealed record ApplicationSummary(
    string Repository,
    int TotalRuns,
    int ReleasedCount,
    int RunningCount,
    int FailedCount,
    int PendingCount,
    int BlockedCount,
    DateTimeOffset? LatestActivityUtc,
    RunSummary? LatestRun)
{
    public string RepositoryUrl => $"https://github.com/{Repository}";

    // Display health rolls up the run-status breakdown to a single chip slug.
    public string HealthSlug
    {
        get
        {
            if (FailedCount > 0) return "failed";
            if (BlockedCount > 0) return "blocked";
            if (RunningCount > 0) return "running";
            if (ReleasedCount > 0) return "released";
            return "pending";
        }
    }

    public string HealthLabel => HealthSlug switch
    {
        "failed"   => "Has failures",
        "blocked"  => "Awaiting human input",
        "running"  => "Active",
        "released" => "Healthy",
        _          => "Idle"
    };
}
