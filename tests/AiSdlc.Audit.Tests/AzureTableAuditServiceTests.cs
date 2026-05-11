using Azure.Data.Tables;
using AiSdlc.Audit;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Audit.Tests;

public sealed class AuditEventEntityMappingTests
{
    private static AuditEvent MakeEvent(string runId = "run-1") => new()
    {
        RunId             = runId,
        TimestampUtc      = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
        Repository        = "org/repo",
        IssueNumber       = 42,
        PullRequestNumber = 7,
        ActorType         = "Agent",
        ActorName         = "BusinessAnalyst",
        Action            = "ReviewCompleted",
        Summary           = "Spec looks good.",
        Decision          = "Approve",
        RiskLevel         = "Low",
        CommitSha         = "abc123",
        RedactionApplied  = true,
        References        = new Dictionary<string, string> { ["specUrl"] = "https://example.com" }
    };

    [Fact]
    public void ToTableEntity_SetsPartitionKeyToRunId()
    {
        var e      = MakeEvent("my-run");
        var entity = AzureTableAuditService.ToTableEntity(e);
        Assert.Equal("my-run", entity.PartitionKey);
    }

    private static AuditEvent MakeEventAt(DateTimeOffset ts) => new()
    {
        RunId = "run-1", TimestampUtc = ts, Repository = "org/repo", IssueNumber = 1,
        ActorType = "Agent", ActorName = "BA", Action = "Done", Summary = "Done."
    };

    [Fact]
    public void ToTableEntity_RowKeyIsChronologicallySortable()
    {
        var earlier = MakeEventAt(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var later   = MakeEventAt(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var rowKeyEarlier = AzureTableAuditService.ToTableEntity(earlier).RowKey;
        var rowKeyLater   = AzureTableAuditService.ToTableEntity(later).RowKey;

        Assert.True(string.Compare(rowKeyEarlier, rowKeyLater, StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = MakeEvent();
        var entity   = AzureTableAuditService.ToTableEntity(original);
        var restored = AzureTableAuditService.FromTableEntity(entity);

        Assert.Equal(original.RunId,             restored.RunId);
        Assert.Equal(original.TimestampUtc,      restored.TimestampUtc);
        Assert.Equal(original.Repository,        restored.Repository);
        Assert.Equal(original.IssueNumber,       restored.IssueNumber);
        Assert.Equal(original.PullRequestNumber, restored.PullRequestNumber);
        Assert.Equal(original.ActorType,         restored.ActorType);
        Assert.Equal(original.ActorName,         restored.ActorName);
        Assert.Equal(original.Action,            restored.Action);
        Assert.Equal(original.Summary,           restored.Summary);
        Assert.Equal(original.Decision,          restored.Decision);
        Assert.Equal(original.RiskLevel,         restored.RiskLevel);
        Assert.Equal(original.CommitSha,         restored.CommitSha);
        Assert.Equal(original.RedactionApplied,  restored.RedactionApplied);
        Assert.Equal(original.References,        restored.References);
    }

    [Fact]
    public void RoundTrip_NullableFieldsOmittedWhenNull()
    {
        var minimal = new AuditEvent
        {
            RunId      = "r",
            Repository = "o/r",
            IssueNumber = 1,
            ActorType  = "System",
            ActorName  = "Orchestrator",
            Action     = "Started",
            Summary    = "Run started."
        };

        var entity   = AzureTableAuditService.ToTableEntity(minimal);
        var restored = AzureTableAuditService.FromTableEntity(entity);

        Assert.Null(restored.PullRequestNumber);
        Assert.Null(restored.Decision);
        Assert.Null(restored.RiskLevel);
        Assert.Null(restored.CommitSha);
        Assert.False(restored.RedactionApplied);
        Assert.Empty(restored.References);
    }
}

[Collection("Azurite")]
public sealed class AzureTableAuditServiceIntegrationTests
{
    private const string AzuriteConnection = "UseDevelopmentStorage=true";
    private const string TableName = "AuditEventsTest";

    // Skip when Azurite is not running (CI without Docker, local without emulator)
    private static readonly bool AzuriteAvailable =
        Environment.GetEnvironmentVariable("AZURITE_AVAILABLE") == "true";

    private static TableClient CreateClient()
    {
        var service = new TableServiceClient(AzuriteConnection);
        var client  = service.GetTableClient(TableName);
        client.CreateIfNotExists();
        return client;
    }

    [SkippableFact]
    public async Task WriteAsync_ThenGetByRunId_ReturnsStoredEvent()
    {
        Skip.IfNot(AzuriteAvailable, "Azurite not available (set AZURITE_AVAILABLE=true to run)");

        var client  = CreateClient();
        var service = new AzureTableAuditService(client);
        var runId   = Guid.NewGuid().ToString();

        var ev = new AuditEvent
        {
            RunId       = runId,
            Repository  = "org/repo",
            IssueNumber = 1,
            ActorType   = "Agent",
            ActorName   = "BusinessAnalyst",
            Action      = "Reviewed",
            Summary     = "All good."
        };

        var result = await service.WriteAsync(ev, CancellationToken.None);

        Assert.Equal(runId, result.RunId);
        Assert.Equal(1, result.EventCountForRun);

        var fetched = await service.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(fetched);
        Assert.Equal("Reviewed", fetched[0].Action);
    }

    [SkippableFact]
    public async Task WriteAsync_MultipleEvents_CountIncrementsCorrectly()
    {
        Skip.IfNot(AzuriteAvailable, "Azurite not available (set AZURITE_AVAILABLE=true to run)");

        var client  = CreateClient();
        var service = new AzureTableAuditService(client);
        var runId   = Guid.NewGuid().ToString();

        AuditEvent Make(string action) => new()
        {
            RunId = runId, Repository = "o/r", IssueNumber = 1,
            ActorType = "Agent", ActorName = "BA", Action = action, Summary = action
        };

        var r1 = await service.WriteAsync(Make("Step1"), CancellationToken.None);
        var r2 = await service.WriteAsync(Make("Step2"), CancellationToken.None);
        var r3 = await service.WriteAsync(Make("Step3"), CancellationToken.None);

        Assert.Equal(1, r1.EventCountForRun);
        Assert.Equal(2, r2.EventCountForRun);
        Assert.Equal(3, r3.EventCountForRun);
    }
}
