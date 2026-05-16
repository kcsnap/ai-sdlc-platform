using System.Text;
using Azure;
using Azure.Storage.Blobs;

namespace AiSdlc.Audit;

public sealed class BlobContextStore : IContextStore
{
    private const string Prefix = "ctx:";
    private readonly BlobContainerClient _container;

    public BlobContextStore(BlobContainerClient container)
    {
        _container = container;
    }

    public async Task<string> OffloadAsync(string runId, string key, string content, CancellationToken ct)
    {
        var blob = _container.GetBlobClient($"{runId}/{key}.md");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await blob.UploadAsync(stream, overwrite: true, ct);
        return $"{Prefix}{runId}/{key}";
    }

    public async Task<string> ResolveAsync(string reference, CancellationToken ct)
    {
        var blobName = reference[Prefix.Length..] + ".md";
        var blob = _container.GetBlobClient(blobName);
        var result = await blob.DownloadContentAsync(ct);
        return result.Value.Content.ToString();
    }

    public bool IsReference(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
}
