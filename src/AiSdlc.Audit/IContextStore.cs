namespace AiSdlc.Audit;

public interface IContextStore
{
    Task<string> OffloadAsync(string runId, string key, string content, CancellationToken ct);
    Task<string> ResolveAsync(string reference, CancellationToken ct);
    bool IsReference(string? value);
}
