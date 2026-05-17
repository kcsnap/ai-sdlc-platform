using AiSdlc.Dashboard.Services;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Dashboard.Tests;

public sealed class DashboardEventBusTests
{
    [Fact]
    public async Task PublishAsync_DeliversBatchToSubscribers()
    {
        var bus = new DashboardEventBus(capacity: 10);
        var received = new List<DashboardEvent>();

        using var _ = bus.Subscribe(batch =>
        {
            received.AddRange(batch);
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new[] { MakeEvent("a"), MakeEvent("b") });

        Assert.Equal(2, received.Count);
    }

    [Fact]
    public async Task Snapshot_ReturnsRecentEventsUpToCapacity()
    {
        var bus = new DashboardEventBus(capacity: 3);

        await bus.PublishAsync(new[] { MakeEvent("1"), MakeEvent("2"), MakeEvent("3"), MakeEvent("4") });

        var snapshot = bus.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(new[] { "2", "3", "4" }, snapshot.Select(e => e.Action));
    }

    [Fact]
    public async Task Subscribe_DisposalStopsFurtherDelivery()
    {
        var bus = new DashboardEventBus(capacity: 10);
        var count = 0;
        var sub = bus.Subscribe(_ => { count++; return Task.CompletedTask; });

        await bus.PublishAsync(new[] { MakeEvent("a") });
        sub.Dispose();
        await bus.PublishAsync(new[] { MakeEvent("b") });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PublishAsync_FailingSubscriberDoesNotBlockOthers()
    {
        var bus = new DashboardEventBus(capacity: 10);
        var goodCount = 0;

        using var _bad = bus.Subscribe(_ => throw new InvalidOperationException("boom"));
        using var _good = bus.Subscribe(_ => { goodCount++; return Task.CompletedTask; });

        await bus.PublishAsync(new[] { MakeEvent("x") });

        Assert.Equal(1, goodCount);
    }

    private static DashboardEvent MakeEvent(string action) =>
        DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId       = "run",
            Repository  = "org/repo",
            IssueNumber = 1,
            ActorType   = "Agent",
            ActorName   = "BusinessAnalyst",
            Action      = action,
            Summary     = action
        });
}
