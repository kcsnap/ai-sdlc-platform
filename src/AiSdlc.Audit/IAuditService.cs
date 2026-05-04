using AiSdlc.Shared;

namespace AiSdlc.Audit;

public interface IAuditService
{
    Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken cancellationToken);
}
