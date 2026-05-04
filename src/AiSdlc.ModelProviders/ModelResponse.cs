namespace AiSdlc.ModelProviders;

public sealed record ModelResponse
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public required string ResponseText { get; init; }
    public Dictionary<string, object> Usage { get; init; } = new();
    public bool WasTruncated { get; init; }
    public List<string> Warnings { get; init; } = new();
}
