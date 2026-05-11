using System.Text;
using Azure;
using Azure.Storage.Blobs;

namespace AiSdlc.Audit;

public sealed class BlobPromptStore : IBlobPromptStore
{
    private readonly BlobContainerClient _container;

    public BlobPromptStore(BlobContainerClient container)
    {
        _container = container;
    }

    public async Task StoreAsync(string runId, string agentName, string prompt, string response, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        await UploadTextAsync(BlobName(runId, agentName, "prompt"), prompt, cancellationToken);
        await UploadTextAsync(BlobName(runId, agentName, "response"), response, cancellationToken);
    }

    public async Task<PromptRecord?> GetAsync(string runId, string agentName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        try
        {
            var prompt   = await DownloadTextAsync(BlobName(runId, agentName, "prompt"),   cancellationToken);
            var response = await DownloadTextAsync(BlobName(runId, agentName, "response"), cancellationToken);
            return new PromptRecord(prompt, response);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task UploadTextAsync(string blobName, string content, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken);
    }

    private async Task<string> DownloadTextAsync(string blobName, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);
        var result = await blob.DownloadContentAsync(cancellationToken);
        return result.Value.Content.ToString();
    }

    private static string BlobName(string runId, string agentName, string kind) =>
        $"{runId}/{agentName}/{kind}.txt";
}
