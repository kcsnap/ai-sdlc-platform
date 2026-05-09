using AiSdlc.Shared;

namespace AiSdlc.Risk;

public sealed record RiskSignal
{
    public required string Code { get; init; }
    public required RiskLevel Level { get; init; }
    public required string Description { get; init; }
}
