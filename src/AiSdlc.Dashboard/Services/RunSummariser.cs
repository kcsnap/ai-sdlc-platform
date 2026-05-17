namespace AiSdlc.Dashboard.Services;

// Folds a flat stream of DashboardEvent into one RunSummary per workflow run.
//
// A "run" is keyed by RunId (which the orchestrator builds as {repo}_{issue}). Status is derived
// from terminal signals in the audit trail:
//   - Released: a ReleaseManager Completed event exists
//   - Failed:   the latest event for any agent is Failed (and not later resolved by a Completed)
//   - Running:  any agent has Started or Completed
//   - Pending:  only webhook/system events so far
public static class RunSummariser
{
    public static IReadOnlyList<RunSummary> Summarise(IReadOnlyList<DashboardEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return Array.Empty<RunSummary>();
        }

        var byRun = events.GroupBy(e => e.RunId);
        var summaries = new List<RunSummary>();

        foreach (var group in byRun)
        {
            var ordered = group.OrderBy(e => e.TimestampUtc).ToArray();
            var latest  = ordered[^1];

            // Latest outcome per agent → for failure detection
            var latestPerAgent = new Dictionary<string, DashboardEvent>(StringComparer.Ordinal);
            foreach (var ev in ordered)
            {
                if (!IsAgent(ev)) continue;
                if (!IsTerminal(ev.Action)) continue;
                if (!latestPerAgent.TryGetValue(ev.ActorName, out var existing)
                    || ev.TimestampUtc > existing.TimestampUtc)
                {
                    latestPerAgent[ev.ActorName] = ev;
                }
            }

            var hasUnresolvedFailure = latestPerAgent.Values.Any(e =>
                string.Equals(e.Action, "Failed", StringComparison.OrdinalIgnoreCase));

            var hasReleaseManagerCompleted = ordered.Any(e =>
                IsAgent(e)
                && e.ActorName.Replace(" ", "").Replace("/", "").Equals("ReleaseManager", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Action, "Completed", StringComparison.OrdinalIgnoreCase));

            var hasAnyAgentActivity = ordered.Any(IsAgent);

            var status = hasReleaseManagerCompleted
                ? RunStatus.Released
                : hasUnresolvedFailure
                    ? RunStatus.Failed
                    : hasAnyAgentActivity
                        ? RunStatus.Running
                        : RunStatus.Pending;

            var pr = ordered
                .Where(e => e.PullRequestNumber.HasValue)
                .OrderByDescending(e => e.TimestampUtc)
                .Select(e => e.PullRequestNumber)
                .FirstOrDefault();

            var distinctAgents = ordered.Where(IsAgent).Select(e => e.ActorName).Distinct().Count();
            var failedCount    = ordered.Count(e => string.Equals(e.Action, "Failed", StringComparison.OrdinalIgnoreCase));

            // Retries = Failed events that were eventually followed by a later Completed for the same agent
            var retryCount = 0;
            foreach (var ev in ordered)
            {
                if (!IsAgent(ev) || !string.Equals(ev.Action, "Failed", StringComparison.OrdinalIgnoreCase))
                    continue;
                var laterCompleted = ordered.Any(o =>
                    IsAgent(o)
                    && string.Equals(o.ActorName, ev.ActorName, StringComparison.Ordinal)
                    && string.Equals(o.Action, "Completed", StringComparison.OrdinalIgnoreCase)
                    && o.TimestampUtc > ev.TimestampUtc);
                if (laterCompleted) retryCount++;
            }

            // The earliest webhook event for this run carries the title (extracted by DashboardEvent).
            var issueTitle = ordered
                .Where(e => !string.IsNullOrWhiteSpace(e.IssueTitle))
                .OrderBy(e => e.TimestampUtc)
                .Select(e => e.IssueTitle)
                .FirstOrDefault();

            // Issue state from GitHub — latest webhook wins so reopens/closes are reflected accurately.
            var latestStateEvent = ordered
                .Where(e => !string.IsNullOrWhiteSpace(e.IssueState))
                .OrderByDescending(e => e.TimestampUtc)
                .FirstOrDefault();

            summaries.Add(new RunSummary(
                RunId:               group.Key,
                Repository:          latest.Repository,
                IssueNumber:         ordered[0].IssueNumber,
                PullRequestNumber:   pr,
                IssueTitle:          issueTitle,
                IssueState:          latestStateEvent?.IssueState,
                IssueStateReason:    latestStateEvent?.IssueStateReason,
                FirstActivityUtc:    ordered[0].TimestampUtc,
                LatestActivityUtc:   latest.TimestampUtc,
                EventCount:          ordered.Length,
                AgentCount:          distinctAgents,
                FailedEventCount:    failedCount,
                RetryCount:          retryCount,
                Status:              status,
                LatestActor:         latest.ActorName,
                LatestAction:        latest.Action,
                LatestSummary:       latest.Summary));
        }

        summaries.Sort(static (a, b) => b.LatestActivityUtc.CompareTo(a.LatestActivityUtc));
        return summaries;
    }

    private static bool IsAgent(DashboardEvent e) =>
        string.Equals(e.ActorType, "Agent", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string action) =>
        string.Equals(action, "Completed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(action, "Failed",    StringComparison.OrdinalIgnoreCase);
}
