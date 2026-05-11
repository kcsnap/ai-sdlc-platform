namespace AiSdlc.Audit;

public interface IBlobPromptStore
{
    Task StoreAsync(string runId, string agentName, string prompt, string response, CancellationToken cancellationToken);
    Task<PromptRecord?> GetAsync(string runId, string agentName, CancellationToken cancellationToken);
}

public sealed record PromptRecord(string Prompt, string Response);
