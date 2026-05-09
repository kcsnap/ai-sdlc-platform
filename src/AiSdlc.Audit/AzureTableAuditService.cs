using System.Text.Json;
using Azure.Data.Tables;
using AiSdlc.Shared;

namespace AiSdlc.Audit;

public sealed class AzureTableAuditService : IAuditService
{
    private readonly TableClient _table;

    public AzureTableAuditService(TableClient table)
    {
        _table = table;
    }

    public async Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var entity = ToTableEntity(auditEvent);
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

        var count = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {auditEvent.RunId}");
        await foreach (var _ in _table.QueryAsync<TableEntity>(
            filter: filter,
            select: ["RowKey"],
            cancellationToken: cancellationToken))
        {
            count++;
        }

        return new AuditWriteResult
        {
            RunId = auditEvent.RunId,
            StoredAtUtc = auditEvent.TimestampUtc,
            EventCountForRun = count
        };
    }

    public async Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var events = new List<AuditEvent>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {runId}");
        await foreach (var entity in _table.QueryAsync<TableEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            events.Add(FromTableEntity(entity));
        }

        return events.AsReadOnly();
    }

    internal static TableEntity ToTableEntity(AuditEvent e)
    {
        // RowKey is chronologically sortable; GUID suffix guarantees uniqueness within same tick
        var rowKey = $"{e.TimestampUtc.UtcTicks:D20}_{Guid.NewGuid():N}";

        var entity = new TableEntity(e.RunId, rowKey)
        {
            ["EventTimestampUtc"] = e.TimestampUtc.UtcDateTime,
            ["Repository"]        = e.Repository,
            ["IssueNumber"]       = e.IssueNumber,
            ["ActorType"]         = e.ActorType,
            ["ActorName"]         = e.ActorName,
            ["Action"]            = e.Action,
            ["Summary"]           = e.Summary,
            ["RedactionApplied"]  = e.RedactionApplied,
            ["References"]        = JsonSerializer.Serialize(e.References)
        };

        if (e.PullRequestNumber.HasValue) entity["PullRequestNumber"] = e.PullRequestNumber.Value;
        if (e.Decision   is not null)     entity["Decision"]           = e.Decision;
        if (e.RiskLevel  is not null)     entity["RiskLevel"]          = e.RiskLevel;
        if (e.CommitSha  is not null)     entity["CommitSha"]          = e.CommitSha;

        return entity;
    }

    internal static AuditEvent FromTableEntity(TableEntity entity)
    {
        var refsJson = entity.GetString("References") ?? "{}";
        var refs = JsonSerializer.Deserialize<Dictionary<string, string>>(refsJson) ?? [];

        return new AuditEvent
        {
            RunId             = entity.PartitionKey,
            TimestampUtc      = entity.GetDateTimeOffset("EventTimestampUtc") ?? DateTimeOffset.MinValue,
            Repository        = entity.GetString("Repository")   ?? string.Empty,
            IssueNumber       = entity.GetInt32("IssueNumber")   ?? 0,
            PullRequestNumber = entity.GetInt32("PullRequestNumber"),
            ActorType         = entity.GetString("ActorType")    ?? string.Empty,
            ActorName         = entity.GetString("ActorName")    ?? string.Empty,
            Action            = entity.GetString("Action")       ?? string.Empty,
            Summary           = entity.GetString("Summary")      ?? string.Empty,
            Decision          = entity.GetString("Decision"),
            RiskLevel         = entity.GetString("RiskLevel"),
            CommitSha         = entity.GetString("CommitSha"),
            RedactionApplied  = entity.GetBoolean("RedactionApplied") ?? false,
            References        = refs
        };
    }
}
