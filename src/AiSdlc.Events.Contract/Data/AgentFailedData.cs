namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Persona agent raised an exception.
/// </summary>
/// <param name="AgentName">Friendly agent name.</param>
/// <param name="Summary">Human-readable failure summary (typically the exception message).</param>
/// <param name="ExceptionType">Optional CLR exception type name.</param>
/// <param name="StackTrace">Optional stack trace (truncated to 30 KB at source to stay under Azure Table Storage's 64 KB property limit).</param>
public sealed record AgentFailedData(
    string AgentName,
    string Summary,
    string? ExceptionType = null,
    string? StackTrace = null) : EventData;
