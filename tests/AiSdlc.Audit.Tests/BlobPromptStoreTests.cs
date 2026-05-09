using Azure.Storage.Blobs;
using AiSdlc.Audit;
using Xunit;

namespace AiSdlc.Audit.Tests;

[Collection("Azurite")]
public sealed class BlobPromptStoreTests
{
    private const string AzuriteConnection  = "UseDevelopmentStorage=true";
    private const string ContainerName      = "prompts-test";

    private static readonly bool AzuriteAvailable =
        Environment.GetEnvironmentVariable("AZURITE_AVAILABLE") == "true";

    private static BlobContainerClient CreateContainer()
    {
        var client = new BlobContainerClient(AzuriteConnection, ContainerName);
        client.CreateIfNotExists();
        return client;
    }

    [SkippableFact]
    public async Task StoreAsync_ThenGetAsync_ReturnsSameContent()
    {
        Skip.IfNot(AzuriteAvailable, "Azurite not available (set AZURITE_AVAILABLE=true to run)");

        var store   = new BlobPromptStore(CreateContainer());
        var runId   = Guid.NewGuid().ToString();
        var agent   = "BusinessAnalyst";
        var prompt  = "Summarise the spec.";
        var response = "The spec covers authentication and payments.";

        await store.StoreAsync(runId, agent, prompt, response, CancellationToken.None);

        var record = await store.GetAsync(runId, agent, CancellationToken.None);
        Assert.NotNull(record);
        Assert.Equal(prompt,   record.Prompt);
        Assert.Equal(response, record.Response);
    }

    [SkippableFact]
    public async Task GetAsync_WhenNotStored_ReturnsNull()
    {
        Skip.IfNot(AzuriteAvailable, "Azurite not available (set AZURITE_AVAILABLE=true to run)");

        var store  = new BlobPromptStore(CreateContainer());
        var result = await store.GetAsync(Guid.NewGuid().ToString(), "NonExistentAgent", CancellationToken.None);
        Assert.Null(result);
    }

    [SkippableFact]
    public async Task StoreAsync_Overwrites_PreviousContent()
    {
        Skip.IfNot(AzuriteAvailable, "Azurite not available (set AZURITE_AVAILABLE=true to run)");

        var store = new BlobPromptStore(CreateContainer());
        var runId = Guid.NewGuid().ToString();
        var agent = "ProductOwner";

        await store.StoreAsync(runId, agent, "original prompt", "original response", CancellationToken.None);
        await store.StoreAsync(runId, agent, "updated prompt",  "updated response",  CancellationToken.None);

        var record = await store.GetAsync(runId, agent, CancellationToken.None);
        Assert.Equal("updated prompt",   record!.Prompt);
        Assert.Equal("updated response", record.Response);
    }
}
