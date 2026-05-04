namespace AiSdlc.Shared;

/// <summary>
/// Represents the decision made by the risk assessment stage.
/// </summary>
public enum RiskDecision
{
    Unknown,
    ContinueAutonomously,
    RequireHumanReview,
    StopWorkflow
}
