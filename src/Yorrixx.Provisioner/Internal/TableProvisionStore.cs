using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Yorrixx.Provisioner.Contracts;

namespace Yorrixx.Provisioner.Internal;

/// Status held per buildId so the GET /provision/{buildId} poll fallback can report progress even if the
/// provision-result callback was dropped. Persisted in Table storage (replaces the old in-proc
/// ConcurrentDictionary) so it survives host restarts — the Functions-host equivalent of the platform's
/// Durable instance state.
public sealed record ProvisionStatus(string Status, ProvisionResult? Result);

public sealed class TableProvisionStore
{
    public const string TableName = "ProvisionStatus";
    private const string Partition = "provision";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly TableClient _table;

    public TableProvisionStore(TableClient table)
    {
        _table = table;
        _table.CreateIfNotExists();
    }

    public void SetAccepted(string buildId) => Upsert(buildId, "accepted", result: null);

    public void SetResult(string buildId, ProvisionResult result) =>
        Upsert(buildId, result.Outcome == "provisioned" ? "provisioned" : "failed", result);

    public ProvisionStatus? Get(string buildId)
    {
        try
        {
            var entity = _table.GetEntity<TableEntity>(Partition, buildId).Value;
            var status = entity.GetString("Status") ?? "accepted";
            var json   = entity.GetString("ResultJson");
            var result = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<ProvisionResult>(json, Json);
            return new ProvisionStatus(status, result);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private void Upsert(string buildId, string status, ProvisionResult? result)
    {
        var entity = new TableEntity(Partition, buildId)
        {
            ["Status"]     = status,
            ["ResultJson"] = result is null ? null : JsonSerializer.Serialize(result, Json),
        };
        _table.UpsertEntity(entity, TableUpdateMode.Replace);
    }
}
