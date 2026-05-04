namespace AiSdlc.GitHub;

public sealed record ChangedFile
{
    public required string Path { get; init; }
    public required string Status { get; init; }
    public int Additions { get; init; }
    public int Deletions { get; init; }
    public int Changes { get; init; }
}
