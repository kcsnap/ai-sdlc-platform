namespace AiSdlc.Dashboard.Services;

// Folds the raw audit-event stream into one row per "agent execution".
//
// An execution = a contiguous Started → (Failed|Completed) sequence for a single (RunId, ActorName).
// Durable retries appear as Started → Failed → Started → Failed → … → Started → (Completed|Failed)
// and are folded into a single row showing the final outcome. Earlier Failed attempts move into the
// row's PriorAttempts list so the drill-down can show what went wrong on each retry.
//
// A Completed event closes the execution — a subsequent Started for the same agent in the same run
// begins a *new* execution (e.g. an agent that the workflow legitimately invokes twice).
//
// Non-agent events (Webhook, System) are emitted unchanged with an empty PriorAttempts list.
public static class FeedGrouper
{
    public static IReadOnlyList<FeedRow> Group(IReadOnlyList<DashboardEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return Array.Empty<FeedRow>();
        }

        var agentRows    = new List<FeedRow>();
        var nonAgentRows = new List<FeedRow>();

        // Bucket agent events by (RunId, ActorName) so each agent's timeline is processed independently.
        var byAgentKey = new Dictionary<(string RunId, string ActorName), List<DashboardEvent>>();

        foreach (var ev in events)
        {
            if (string.Equals(ev.ActorType, "Agent", StringComparison.OrdinalIgnoreCase))
            {
                var key = (ev.RunId, ev.ActorName);
                if (!byAgentKey.TryGetValue(key, out var list))
                {
                    list = new List<DashboardEvent>();
                    byAgentKey[key] = list;
                }
                list.Add(ev);
            }
            else
            {
                nonAgentRows.Add(new FeedRow(ev, Array.Empty<DashboardEvent>()));
            }
        }

        foreach (var (_, agentEvents) in byAgentKey)
        {
            agentEvents.Sort(static (a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

            var currentExecution = new List<DashboardEvent>();
            foreach (var ev in agentEvents)
            {
                var isCloseAndRestart =
                    currentExecution.Count > 0
                    && string.Equals(currentExecution[^1].Action, "Completed", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ev.Action, "Started", StringComparison.OrdinalIgnoreCase);

                if (isCloseAndRestart)
                {
                    agentRows.Add(BuildExecutionRow(currentExecution));
                    currentExecution = new List<DashboardEvent>();
                }

                currentExecution.Add(ev);
            }

            if (currentExecution.Count > 0)
            {
                agentRows.Add(BuildExecutionRow(currentExecution));
            }
        }

        // Merge and emit newest-first to match the live feed's display order.
        var all = new List<FeedRow>(agentRows.Count + nonAgentRows.Count);
        all.AddRange(agentRows);
        all.AddRange(nonAgentRows);
        all.Sort(static (a, b) => b.Display.TimestampUtc.CompareTo(a.Display.TimestampUtc));
        return all;
    }

    private static FeedRow BuildExecutionRow(IReadOnlyList<DashboardEvent> execution)
    {
        // Pick the event we want to *show* as the visible row:
        //   1. A Completed (success) wins — agent ultimately succeeded.
        //   2. Otherwise the latest Failed — all attempts failed; show the final error.
        //   3. Otherwise the latest Started — agent is in-flight, no outcome yet.
        DashboardEvent? completed = null;
        DashboardEvent? latestFailed = null;
        DashboardEvent? latestStarted = null;

        foreach (var ev in execution)
        {
            if (string.Equals(ev.Action, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                completed = ev;
            }
            else if (string.Equals(ev.Action, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                if (latestFailed is null || ev.TimestampUtc > latestFailed.TimestampUtc)
                    latestFailed = ev;
            }
            else if (string.Equals(ev.Action, "Started", StringComparison.OrdinalIgnoreCase))
            {
                if (latestStarted is null || ev.TimestampUtc > latestStarted.TimestampUtc)
                    latestStarted = ev;
            }
        }

        var display = completed ?? latestFailed ?? latestStarted!;

        // Prior attempts = all Failed events that aren't the displayed row, oldest-first so the
        // drill-down reads chronologically (attempt 1, attempt 2, …).
        var prior = execution
            .Where(e => string.Equals(e.Action, "Failed", StringComparison.OrdinalIgnoreCase) && e.Id != display.Id)
            .OrderBy(e => e.TimestampUtc)
            .ToArray();

        return new FeedRow(display, prior);
    }
}
