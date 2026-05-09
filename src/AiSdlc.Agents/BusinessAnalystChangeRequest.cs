namespace AiSdlc.Agents;

public sealed record BusinessAnalystChangeRequest
{
    public string? Title { get; init; }
    public string ChangeRequest { get; init; } = string.Empty;
    public string BusinessNeed { get; init; } = string.Empty;
    public string TargetUser { get; init; } = string.Empty;
    public string AppType { get; init; } = string.Empty;
    public string Constraints { get; init; } = string.Empty;
    public string ReferenceMaterial { get; init; } = string.Empty;
    public string DefinitionOfDone { get; init; } = string.Empty;
    public string ExistingProductContext { get; init; } = string.Empty;
    public string RawSpecMarkdown { get; init; } = string.Empty;
}
