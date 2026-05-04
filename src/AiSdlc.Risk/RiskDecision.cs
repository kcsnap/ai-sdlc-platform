using AiSdlc.Shared;

namespace AiSdlc.Risk;

public sealed class RiskDecision
{
    public required RiskLevel Level { get; init; }
    public required string Decision { get; init; }
    public required string Rationale { get; init; }
    public bool HumanReviewRequired { get; init; }
}
