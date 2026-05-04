using AiSdlc.Shared;

namespace AiSdlc.Audit;

public sealed class InMemoryAuditService : IAuditService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, List<AuditEvent>> _eventsByRunId = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_eventsByRunId.TryGetValue(runId, out var auditEvents))
            {
                return Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
            }

            return Task.FromResult<IReadOnlyList<AuditEvent>>(auditEvents.ToArray());
        }
    }

    public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_eventsByRunId.TryGetValue(auditEvent.RunId, out var auditEvents))
            {
                auditEvents = new List<AuditEvent>();
                _eventsByRunId[auditEvent.RunId] = auditEvents;
            }

            auditEvents.Add(auditEvent);

            return Task.FromResult(new AuditWriteResult
            {
                RunId = auditEvent.RunId,
                StoredAtUtc = DateTimeOffset.UtcNow,
                EventCountForRun = auditEvents.Count
            });
        }
    }
}
