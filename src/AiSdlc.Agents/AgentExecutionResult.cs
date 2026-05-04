using AiSdlc.Shared;

namespace AiSdlc.Agents;

public sealed record AgentExecutionResult
{
    public required bool Succeeded { get; init; }
    public required string AgentName { get; init; }
    public AgentResult? Result { get; init; }
    public string? ErrorMessage { get; init; }
}
