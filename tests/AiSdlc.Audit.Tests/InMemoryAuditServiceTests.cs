using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Audit.Tests;

public sealed class InMemoryAuditServiceTests
{
    [Fact]
    public async Task WriteAsync_ShouldStoreAuditEventAndReturnWriteMetadata()
    {
        var service = new InMemoryAuditService();
        var auditEvent = CreateAuditEvent(runId: "run-123", action: "risk-assessed");

        var result = await service.WriteAsync(auditEvent, CancellationToken.None);

        Assert.Equal("run-123", result.RunId);
        Assert.Equal(1, result.EventCountForRun);
        Assert.True(result.StoredAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetByRunIdAsync_ShouldReturnEventsForSpecifiedRunOnly()
    {
        var service = new InMemoryAuditService();

        await service.WriteAsync(CreateAuditEvent(runId: "run-123", action: "created"), CancellationToken.None);
        await service.WriteAsync(CreateAuditEvent(runId: "run-123", action: "reviewed"), CancellationToken.None);
        await service.WriteAsync(CreateAuditEvent(runId: "run-999", action: "ignored"), CancellationToken.None);

        var events = await service.GetByRunIdAsync("run-123", CancellationToken.None);

        Assert.Collection(
            events,
            auditEvent => Assert.Equal("created", auditEvent.Action),
            auditEvent => Assert.Equal("reviewed", auditEvent.Action));
    }

    [Fact]
    public async Task GetByRunIdAsync_ShouldReturnEmptyCollectionWhenRunDoesNotExist()
    {
        var service = new InMemoryAuditService();

        var events = await service.GetByRunIdAsync("missing-run", CancellationToken.None);

        Assert.Empty(events);
    }

    private static AuditEvent CreateAuditEvent(string runId, string action) =>
        new()
        {
            RunId = runId,
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 1,
            ActorType = "agent",
            ActorName = "RiskEngine",
            Action = action,
            Summary = $"Audit event for {action}"
        };
}
