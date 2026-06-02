using AiSdlc.Shared;

namespace AiSdlc.Audit;

public sealed class InMemoryAuditService : IAuditService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, List<StoredAuditEvent>> _storedByRunId = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_storedByRunId.TryGetValue(runId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
            }

            return Task.FromResult<IReadOnlyList<AuditEvent>>(stored.Select(s => s.Event).ToArray());
        }
    }

    public Task<IReadOnlyList<AuditEvent>> GetSinceAsync(DateTimeOffset since, int maxResults, CancellationToken cancellationToken)
    {
        if (maxResults <= 0)
        {
            return Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _storedByRunId.Values
                .SelectMany(list => list)
                .Select(s => s.Event)
                .Where(e => e.TimestampUtc > since)
                .OrderBy(e => e.TimestampUtc)
                .Take(maxResults)
                .ToArray();

            return Task.FromResult<IReadOnlyList<AuditEvent>>(matches);
        }
    }

    public Task<IReadOnlyList<StoredAuditEvent>> GetByRunIdAfterRowKeyAsync(
        string runId,
        string? rowKeyExclusive,
        int maxResults,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (maxResults <= 0)
        {
            return Task.FromResult<IReadOnlyList<StoredAuditEvent>>(Array.Empty<StoredAuditEvent>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_storedByRunId.TryGetValue(runId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<StoredAuditEvent>>(Array.Empty<StoredAuditEvent>());
            }

            var query = stored.OrderBy(s => s.RowKey, StringComparer.Ordinal).AsEnumerable();
            if (!string.IsNullOrEmpty(rowKeyExclusive))
            {
                query = query.Where(s => StringComparer.Ordinal.Compare(s.RowKey, rowKeyExclusive) > 0);
            }

            return Task.FromResult<IReadOnlyList<StoredAuditEvent>>(query.Take(maxResults).ToArray());
        }
    }

    public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_storedByRunId.TryGetValue(auditEvent.RunId, out var stored))
            {
                stored = new List<StoredAuditEvent>();
                _storedByRunId[auditEvent.RunId] = stored;
            }

            var rowKey = $"{auditEvent.TimestampUtc.UtcTicks:D20}_{Guid.NewGuid():N}";
            stored.Add(new StoredAuditEvent(auditEvent, rowKey));

            return Task.FromResult(new AuditWriteResult
            {
                RunId = auditEvent.RunId,
                StoredAtUtc = DateTimeOffset.UtcNow,
                EventCountForRun = stored.Count
            });
        }
    }
}
