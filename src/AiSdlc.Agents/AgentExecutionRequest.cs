using AiSdlc.Shared;

namespace AiSdlc.Agents;

public sealed record AgentExecutionRequest
{
    public required string AgentName { get; init; }
    public required AgentContext Context { get; init; }
}
