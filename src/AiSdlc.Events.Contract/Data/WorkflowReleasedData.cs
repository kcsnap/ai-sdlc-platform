namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Terminal: workflow released successfully (PR merged, deployment recorded).
/// </summary>
/// <param name="Summary">Human-readable summary of the release outcome.</param>
public sealed record WorkflowReleasedData(
    string Summary) : EventData;
