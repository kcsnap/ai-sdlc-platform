namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Terminal: workflow stopped (human stop, gate failure, or workflow-level abort).
/// </summary>
/// <param name="Summary">Human-readable reason for the stop.</param>
/// <param name="Decision">Optional structured decision label (e.g. <c>Stopped</c>, gate name).</param>
public sealed record WorkflowStoppedData(
    string Summary,
    string? Decision = null) : EventData;
