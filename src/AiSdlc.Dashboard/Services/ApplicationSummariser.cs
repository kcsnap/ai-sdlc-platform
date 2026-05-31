namespace AiSdlc.Dashboard.Services;

// Folds the run-level summaries into one entry per repository (= "application").
public static class ApplicationSummariser
{
    public static IReadOnlyList<ApplicationSummary> Summarise(IReadOnlyList<DashboardEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return Array.Empty<ApplicationSummary>();
        }

        // Use the existing RunSummariser so an "application" is just a regrouping of its runs.
        var runs = RunSummariser.Summarise(events);

        return runs
            .GroupBy(r => r.Repository, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ordered     = g.OrderByDescending(r => r.LatestActivityUtc).ToArray();
                var latest      = ordered[0];
                var released    = ordered.Count(r => r.Status == RunStatus.Released);
                var running     = ordered.Count(r => r.Status == RunStatus.Running);
                var failed      = ordered.Count(r => r.Status == RunStatus.Failed);
                var blocked     = ordered.Count(r => r.Status == RunStatus.Blocked);
                var pending     = ordered.Count(r => r.Status == RunStatus.Pending || r.Status == RunStatus.Unknown);

                return new ApplicationSummary(
                    Repository:        g.Key,
                    TotalRuns:         ordered.Length,
                    ReleasedCount:     released,
                    RunningCount:      running,
                    FailedCount:       failed,
                    PendingCount:      pending,
                    BlockedCount:      blocked,
                    LatestActivityUtc: latest.LatestActivityUtc,
                    LatestRun:         latest);
            })
            .OrderByDescending(a => a.LatestActivityUtc)
            .ToArray();
    }
}
