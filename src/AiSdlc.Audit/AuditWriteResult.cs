namespace AiSdlc.Audit;

public sealed record AuditWriteResult
{
    public required string RunId { get; init; }
    public required DateTimeOffset StoredAtUtc { get; init; }
    public required int EventCountForRun { get; init; }
}
