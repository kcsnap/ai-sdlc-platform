namespace AiSdlc.ModelProviders;

public sealed record ModelProviderOptions
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public int DefaultMaxTokens { get; init; } = 2048;
}
