using AiSdlc.Shared;

namespace AiSdlc.Risk;

public sealed class RiskRule
{
    public required string Code { get; init; }
    public required RiskLevel Level { get; init; }
    public required string Description { get; init; }
    public required Func<RiskAssessmentRequest, bool> Matches { get; init; }
}
