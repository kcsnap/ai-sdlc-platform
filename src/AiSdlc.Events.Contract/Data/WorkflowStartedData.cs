namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Orchestrator instance created for the run. All meaningful context lives on the envelope; this payload is intentionally empty.
/// </summary>
public sealed record WorkflowStartedData : EventData;
