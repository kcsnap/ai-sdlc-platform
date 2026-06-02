namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Persona agent finished successfully.
/// </summary>
/// <param name="AgentName">Friendly agent name.</param>
/// <param name="Summary">Human-readable result summary (truncated to 256 chars at source).</param>
/// <param name="Decision">Optional workflow decision (e.g. <c>ContinueAutonomously</c>, <c>RequireHumanReview</c>, <c>AUTO_MERGE_ELIGIBLE</c>).</param>
/// <param name="RiskLevel">Optional risk level (e.g. <c>Low</c>, <c>Medium</c>, <c>High</c>).</param>
/// <param name="CommitSha">Optional git commit hash if the agent produced one.</param>
public sealed record AgentCompletedData(
    string AgentName,
    string Summary,
    string? Decision = null,
    string? RiskLevel = null,
    string? CommitSha = null) : EventData;
