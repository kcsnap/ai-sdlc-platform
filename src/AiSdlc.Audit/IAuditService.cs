using AiSdlc.Shared;

namespace AiSdlc.Audit;

public interface IAuditService
{
    Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken cancellationToken);

    // Returns events with TimestampUtc > since, capped at maxResults, ordered oldest-first.
    // Used by the dashboard to tail the live audit feed across all runs.
    Task<IReadOnlyList<AuditEvent>> GetSinceAsync(DateTimeOffset since, int maxResults, CancellationToken cancellationToken);
}
