namespace AiSdlc.Risk;

public sealed record RiskRule
{
    public required string Code { get; init; }
    public required string Description { get; init; }
}
