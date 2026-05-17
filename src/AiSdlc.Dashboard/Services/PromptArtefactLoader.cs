using AiSdlc.Audit;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Dashboard.Services;

// Lazily fetches prompt/response artefacts from blob storage when a UI row is expanded.
public sealed class PromptArtefactLoader
{
    private readonly IBlobPromptStore _store;
    private readonly ILogger<PromptArtefactLoader> _logger;

    public PromptArtefactLoader(IBlobPromptStore store, ILogger<PromptArtefactLoader> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<PromptRecord?> LoadAsync(string runId, string agentName, CancellationToken cancellationToken)
    {
        try
        {
            return await _store.GetAsync(runId, agentName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load prompt artefact for {RunId}/{Agent}.", runId, agentName);
            return null;
        }
    }
}
