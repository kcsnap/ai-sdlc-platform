namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Terminal: workflow failed with an unrecoverable exception.
/// </summary>
/// <param name="Summary">Human-readable failure summary.</param>
/// <param name="ExceptionType">Optional CLR exception type name.</param>
public sealed record WorkflowFailedData(
    string Summary,
    string? ExceptionType = null) : EventData;
