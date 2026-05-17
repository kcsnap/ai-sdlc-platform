namespace AiSdlc.Dashboard.Services;

public enum AgentVisualState { NotStarted, InProgress, Complete, Failed }

public sealed record StageNodeState(
    string AgentName,
    AgentVisualState State,
    int Attempts,
    DateTimeOffset? LastUpdatedUtc);

public sealed record StageState(string Label, IReadOnlyList<StageNodeState> Nodes);

// Folds the raw event stream into a per-stage / per-agent state for the workflow diagram.
//
// Status rules per agent:
//   No events                                       -> NotStarted
//   Latest terminal event for the agent is Failed  -> Failed
//   Any Completed event exists                      -> Complete
//   Only Started events                             -> InProgress
//
// The synthetic "Merged" terminal node is driven by RunStatus, not audit events.
public static class WorkflowStateBuilder
{
    public static IReadOnlyList<StageState> Build(IReadOnlyList<DashboardEvent> runEvents, RunStatus runStatus)
    {
        ArgumentNullException.ThrowIfNull(runEvents);

        // Index agent events by ActorName for O(1) lookup per known stage agent.
        var byAgent = runEvents
            .Where(e => string.Equals(e.ActorType, "Agent", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.ActorName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.TimestampUtc).ToArray(), StringComparer.Ordinal);

        var stages = new List<StageState>(WorkflowDefinition.Stages.Count);

        foreach (var stage in WorkflowDefinition.Stages)
        {
            var nodes = new List<StageNodeState>(stage.AgentNames.Count);
            foreach (var agentName in stage.AgentNames)
            {
                nodes.Add(agentName == WorkflowDefinition.MergedNodeName
                    ? BuildMergedNode(runStatus, runEvents)
                    : BuildAgentNode(agentName, byAgent));
            }
            stages.Add(new StageState(stage.Label, nodes));
        }

        return stages;
    }

    private static StageNodeState BuildAgentNode(string agentName, IReadOnlyDictionary<string, DashboardEvent[]> byAgent)
    {
        if (!byAgent.TryGetValue(agentName, out var events) || events.Length == 0)
        {
            return new StageNodeState(agentName, AgentVisualState.NotStarted, Attempts: 0, LastUpdatedUtc: null);
        }

        var attempts = events.Count(e => string.Equals(e.Action, "Started", StringComparison.OrdinalIgnoreCase));
        var lastUpdated = events[^1].TimestampUtc;

        // Terminal events only (Completed | Failed) — Started events don't decide final state.
        var terminals = events
            .Where(e => string.Equals(e.Action, "Completed", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(e.Action, "Failed",    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (terminals.Length == 0)
        {
            // Started but never terminated → in flight.
            return new StageNodeState(agentName, AgentVisualState.InProgress, attempts, lastUpdated);
        }

        // Any Completed (anywhere in history) means the agent eventually succeeded.
        var hasCompleted = terminals.Any(e => string.Equals(e.Action, "Completed", StringComparison.OrdinalIgnoreCase));
        if (hasCompleted)
        {
            return new StageNodeState(agentName, AgentVisualState.Complete, attempts, lastUpdated);
        }

        // No Completed, terminals are all Failed → genuine failure.
        return new StageNodeState(agentName, AgentVisualState.Failed, attempts, lastUpdated);
    }

    private static StageNodeState BuildMergedNode(RunStatus runStatus, IReadOnlyList<DashboardEvent> runEvents)
    {
        var state = runStatus switch
        {
            RunStatus.Released => AgentVisualState.Complete,
            RunStatus.Failed   => AgentVisualState.Failed,
            _                  => AgentVisualState.NotStarted
        };

        // For the Merged node, use the latest event's timestamp as a proxy for "last updated"
        // so the tooltip surfaces something useful even though no audit row maps to this node directly.
        DateTimeOffset? lastUpdated = runEvents.Count > 0
            ? runEvents.Max(e => e.TimestampUtc)
            : null;

        return new StageNodeState(WorkflowDefinition.MergedNodeName, state, Attempts: 0, lastUpdated);
    }
}
