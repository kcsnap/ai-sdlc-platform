namespace AiSdlc.ModelProviders;

public sealed record ModelRequest
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
