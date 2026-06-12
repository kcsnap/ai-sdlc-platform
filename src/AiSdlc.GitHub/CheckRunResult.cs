namespace AiSdlc.GitHub;

public sealed record CheckRunResult
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required string Conclusion { get; init; }
    public bool IsRequired { get; init; }
    public string? DetailsUrl { get; init; }
}
