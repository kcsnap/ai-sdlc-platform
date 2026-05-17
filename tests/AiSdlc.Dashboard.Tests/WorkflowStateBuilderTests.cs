using AiSdlc.Dashboard.Services;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Dashboard.Tests;

public sealed class WorkflowStateBuilderTests
{
    [Fact]
    public void Empty_AllAgentsNotStarted_AndContainsAllStages()
    {
        var stages = WorkflowStateBuilder.Build(Array.Empty<DashboardEvent>(), RunStatus.Pending);

        Assert.Equal(WorkflowDefinition.Stages.Count, stages.Count);
        foreach (var stage in stages)
        {
            Assert.All(stage.Nodes, n => Assert.Equal(AgentVisualState.NotStarted, n.State));
        }
    }

    [Fact]
    public void AgentWithOnlyStarted_IsInProgress()
    {
        var ev = MakeAgent(0, "Architect", "Started", "starting");
        var node = NodeFor(WorkflowStateBuilder.Build(new[] { ev }, RunStatus.Running), "Architect");

        Assert.Equal(AgentVisualState.InProgress, node.State);
        Assert.Equal(1, node.Attempts);
    }

    [Fact]
    public void AgentWithStartedThenCompleted_IsComplete()
    {
        var events = new[]
        {
            MakeAgent(0, "Business Analyst", "Started",   "go"),
            MakeAgent(1, "Business Analyst", "Completed", "done")
        };
        var node = NodeFor(WorkflowStateBuilder.Build(events, RunStatus.Running), "Business Analyst");

        Assert.Equal(AgentVisualState.Complete, node.State);
        Assert.Equal(1, node.Attempts);
    }

    [Fact]
    public void AgentWithStartedThenFailed_IsFailed()
    {
        var events = new[]
        {
            MakeAgent(0, "Senior Coder", "Started", "go"),
            MakeAgent(1, "Senior Coder", "Failed",  "boom")
        };
        var node = NodeFor(WorkflowStateBuilder.Build(events, RunStatus.Failed), "Senior Coder");

        Assert.Equal(AgentVisualState.Failed, node.State);
    }

    [Fact]
    public void RetriedAgentEventuallySucceeds_IsCompleteWithAttemptCount()
    {
        var events = new[]
        {
            MakeAgent(0, "Content / SEO Reviewer", "Started",   "a"),
            MakeAgent(1, "Content / SEO Reviewer", "Failed",    "transient 1"),
            MakeAgent(2, "Content / SEO Reviewer", "Started",   "b"),
            MakeAgent(3, "Content / SEO Reviewer", "Failed",    "transient 2"),
            MakeAgent(4, "Content / SEO Reviewer", "Started",   "c"),
            MakeAgent(5, "Content / SEO Reviewer", "Completed", "ok on third try")
        };
        var node = NodeFor(WorkflowStateBuilder.Build(events, RunStatus.Running), "Content / SEO Reviewer");

        Assert.Equal(AgentVisualState.Complete, node.State);
        Assert.Equal(3, node.Attempts);
    }

    [Fact]
    public void MergedNode_CompleteWhenRunReleased()
    {
        var node = NodeFor(WorkflowStateBuilder.Build(Array.Empty<DashboardEvent>(), RunStatus.Released), WorkflowDefinition.MergedNodeName);
        Assert.Equal(AgentVisualState.Complete, node.State);
    }

    [Fact]
    public void MergedNode_FailedWhenRunFailed()
    {
        var node = NodeFor(WorkflowStateBuilder.Build(Array.Empty<DashboardEvent>(), RunStatus.Failed), WorkflowDefinition.MergedNodeName);
        Assert.Equal(AgentVisualState.Failed, node.State);
    }

    [Fact]
    public void MergedNode_NotStartedWhenRunPendingOrRunning()
    {
        Assert.Equal(AgentVisualState.NotStarted,
            NodeFor(WorkflowStateBuilder.Build(Array.Empty<DashboardEvent>(), RunStatus.Pending), WorkflowDefinition.MergedNodeName).State);
        Assert.Equal(AgentVisualState.NotStarted,
            NodeFor(WorkflowStateBuilder.Build(Array.Empty<DashboardEvent>(), RunStatus.Running), WorkflowDefinition.MergedNodeName).State);
    }

    [Fact]
    public void NonAgentEvents_AreIgnoredForAgentStateDerivation()
    {
        // A Webhook event for "Product Strategist" name (unlikely but possible) must not be treated as an agent run.
        var events = new[]
        {
            MakeEvent(0, "Webhook", "/github/webhook", "issues.opened", "ignore me")
        };
        var node = NodeFor(WorkflowStateBuilder.Build(events, RunStatus.Pending), "Product Strategist");
        Assert.Equal(AgentVisualState.NotStarted, node.State);
    }

    private static StageNodeState NodeFor(IReadOnlyList<StageState> stages, string agentName)
    {
        var node = stages.SelectMany(s => s.Nodes).FirstOrDefault(n => n.AgentName == agentName);
        Assert.NotNull(node);
        return node!;
    }

    private static DashboardEvent MakeAgent(int offsetSec, string agent, string action, string summary) =>
        MakeEvent(offsetSec, "Agent", agent, action, summary);

    private static DashboardEvent MakeEvent(int offsetSec, string actorType, string actor, string action, string summary)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero);
        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId        = "run-1",
            TimestampUtc = baseTs.AddSeconds(offsetSec),
            Repository   = "org/repo",
            IssueNumber  = 1,
            ActorType    = actorType,
            ActorName    = actor,
            Action       = action,
            Summary      = summary
        });
    }
}
