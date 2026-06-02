namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Persona agent began executing.
/// </summary>
/// <param name="AgentName">Friendly agent name (e.g. <c>Architect</c>, <c>Risk Assessor</c>).</param>
/// <param name="Summary">Optional human-readable context for the start.</param>
public sealed record AgentStartedData(
    string AgentName,
    string? Summary = null) : EventData;
