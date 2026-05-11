using AiSdlc.Shared;

namespace AiSdlc.Risk;

public sealed record RiskAssessmentResult
{
    public required RiskLevel Level { get; init; }
    public required RiskDecision Decision { get; init; }
    public required string Rationale { get; init; }
    public IReadOnlyList<RiskSignal> TriggeredSignals { get; init; } = [];
}
