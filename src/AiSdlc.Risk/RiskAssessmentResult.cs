using AiSdlc.Shared;

namespace AiSdlc.Risk;

public sealed record RiskAssessmentResult
{
    public required RiskLevel RiskLevel { get; init; }
    public required RiskDecision Decision { get; init; }
    public required string Rationale { get; init; }
    public IReadOnlyList<RiskSignal> TriggeredSignals { get; init; } = Array.Empty<RiskSignal>();
    public IReadOnlyList<RiskRule> TriggeredRules { get; init; } = Array.Empty<RiskRule>();
}
