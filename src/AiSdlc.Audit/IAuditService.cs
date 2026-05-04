using AiSdlc.Shared;

namespace AiSdlc.Audit;

public interface IAuditService
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
