namespace AiSdlc.ModelProviders;

public interface IModelProvider
{
    string ProviderName { get; }
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

public sealed class ModelRequest
{
    public required string AgentName { get; init; }
    public required string TaskType { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public Dictionary<string, string> ContextDocuments { get; init; } = new();
    public string? RequiredOutputSchema { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
}

public sealed class ModelResponse
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public required string ResponseText { get; init; }
    public Dictionary<string, object> Usage { get; init; } = new();
    public bool WasTruncated { get; init; }
    public List<string> Warnings { get; init; } = new();
}
