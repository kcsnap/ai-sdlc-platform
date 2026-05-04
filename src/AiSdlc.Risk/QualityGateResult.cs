namespace AiSdlc.Risk;

public sealed record QualityGateResult
{
    public required string Name { get; init; }
    public bool Passed { get; init; }
    public bool IsMandatory { get; init; } = true;
}
