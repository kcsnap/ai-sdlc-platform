using AiSdlc.Shared;

namespace AiSdlc.Audit;

public interface IAuditService
{
    Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken cancellationToken);

    // Returns events with TimestampUtc > since, capped at maxResults, ordered oldest-first.
    // Used by the dashboard to tail the live audit feed across all runs.
    Task<IReadOnlyList<AuditEvent>> GetSinceAsync(DateTimeOffset since, int maxResults, CancellationToken cancellationToken);

    // Partition-scoped, cursor-paginated read for the per-run events API (ADR-0004).
    // Returns events strictly after the supplied RowKey (null/empty = from beginning), capped at maxResults,
    // ordered oldest-first by RowKey. Each result carries its own RowKey so the caller can build the next cursor.
    Task<IReadOnlyList<StoredAuditEvent>> GetByRunIdAfterRowKeyAsync(
        string runId,
        string? rowKeyExclusive,
        int maxResults,
        CancellationToken cancellationToken);
}
