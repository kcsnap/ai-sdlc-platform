using AiSdlc.Shared;

namespace AiSdlc.Audit;

/// <summary>
/// An audit event as read from storage, paired with the storage-layer RowKey that uniquely identifies it
/// within its partition. RowKey is opaque to higher layers — it becomes the basis for the API's opaque cursor.
/// </summary>
/// <param name="Event">The audit event payload.</param>
/// <param name="RowKey">Storage RowKey — chronologically sortable, collision-free within a single tick.</param>
public sealed record StoredAuditEvent(AuditEvent Event, string RowKey);
