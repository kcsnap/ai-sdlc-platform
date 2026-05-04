using Xunit;

namespace AiSdlc.ModelProviders.Tests;

public sealed class FakeModelProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldReturnDeterministicResponse()
    {
        IModelProvider provider = new FakeModelProvider(new ModelProviderOptions
        {
            ProviderName = "FakeProvider",
            ModelName = "fake-model-v1",
            DefaultMaxTokens = 1024
        });

        var response = await provider.CompleteAsync(
            new ModelRequest
            {
                AgentName = "Product Strategist",
                TaskType = "brief-generation",
                SystemPrompt = "System",
                UserPrompt = "User",
                ContextDocuments = new Dictionary<string, string>
                {
                    ["brief.md"] = "Project brief"
                }
            },
            CancellationToken.None);

        Assert.Equal("FakeProvider", response.ProviderName);
        Assert.Equal("fake-model-v1", response.ModelName);
        Assert.Contains("Product Strategist", response.ResponseText);
        Assert.Equal(1, response.Usage["input_document_count"]);
    }

    [Fact]
    public async Task CompleteAsync_ShouldEmitWarningWhenSchemaIsRequested()
    {
        IModelProvider provider = new FakeModelProvider(new ModelProviderOptions
        {
            ProviderName = "FakeProvider",
            ModelName = "fake-model-v1"
        });

        var response = await provider.CompleteAsync(
            new ModelRequest
            {
                AgentName = "Architect",
                TaskType = "design-review",
                SystemPrompt = "System",
                UserPrompt = "User",
                RequiredOutputSchema = "{ \"type\": \"object\" }"
            },
            CancellationToken.None);

        Assert.Single(response.Warnings);
        Assert.Contains("Schema enforcement", response.Warnings[0]);
    }
}
