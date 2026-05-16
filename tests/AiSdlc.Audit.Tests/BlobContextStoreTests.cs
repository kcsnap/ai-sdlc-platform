using Azure.Storage.Blobs;
using AiSdlc.Audit;
using Xunit;

namespace AiSdlc.Audit.Tests;

[Collection("Azurite")]
public sealed class BlobContextStoreTests
{
    private const string AzuriteConnection = "UseDevelopmentStorage=true";
    private const string ContainerName     = "context-test";

    private static readonly bool AzuriteAvailable =
        Environment.GetEnvironmentVariable("AZURITE_AVAILABLE") == "true";

    private static BlobContainerClient CreateContainer()
    {
        var client = new BlobContainerClient(AzuriteConnection, ContainerName);
        client.CreateIfNotExists();
        return client;
    }

    [SkippableFact]
    public async Task OffloadAsync_ThenResolveAsync_ReturnsSameContent()
    {
        Skip.IfNot(AzuriteAvailable, "Azurite not available (set AZURITE_AVAILABLE=true to run)");

        var store   = new BlobContextStore(CreateContainer());
        var runId   = Guid.NewGuid().ToString();
        var content = "## Architecture\n\nUse a hexagonal pattern.";

        var reference = await store.OffloadAsync(runId, "architectOutput", content, CancellationToken.None);

        Assert.True(store.IsReference(reference));
        Assert.StartsWith("ctx:", reference, StringComparison.Ordinal);

        var resolved = await store.ResolveAsync(reference, CancellationToken.None);
        Assert.Equal(content, resolved);
    }

    [SkippableFact]
    public async Task OffloadAsync_Overwrites_PreviousContent()
    {
        Skip.IfNot(AzuriteAvailable, "Azurite not available (set AZURITE_AVAILABLE=true to run)");

        var store = new BlobContextStore(CreateContainer());
        var runId = Guid.NewGuid().ToString();

        await store.OffloadAsync(runId, "securityOutput", "original", CancellationToken.None);
        var reference = await store.OffloadAsync(runId, "securityOutput", "updated", CancellationToken.None);

        var resolved = await store.ResolveAsync(reference, CancellationToken.None);
        Assert.Equal("updated", resolved);
    }

    [Theory]
    [InlineData("ctx:run123/key")]
    [InlineData("ctx:a/b")]
    public void IsReference_ReturnsTrue_ForCtxPrefixed(string value)
    {
        // IsReference is pure — no container connection needed
        var store = new BlobContextStore(new BlobContainerClient(AzuriteConnection, ContainerName));
        Assert.True(store.IsReference(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some plain text")]
    [InlineData("https://example.com")]
    public void IsReference_ReturnsFalse_ForNonRef(string? value)
    {
        var store = new BlobContextStore(new BlobContainerClient(AzuriteConnection, ContainerName));
        Assert.False(store.IsReference(value));
    }
}
