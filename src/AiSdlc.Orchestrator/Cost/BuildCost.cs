using System.Threading;
using AiSdlc.Agents;

namespace AiSdlc.Orchestrator.Cost;

/// <summary>The cost-telemetry body POSTed to Yorrixx per LLM call (raw Anthropic usage; £ derived downstream).</summary>
public sealed record BuildCostCallback
{
    public string Model { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public int Iteration { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public int Calls { get; init; } = 1;
    public string? RequestId { get; init; }
}

/// <summary>Per-build cost attribution carried across the agent→provider call chain.</summary>
public sealed record CostScope(string AppId, int Iteration);

/// <summary>
/// Ambient cost scope set by the agent-run activity (which knows the app + iteration) and read by the
/// cost-emitting model provider (which sees the token usage). AsyncLocal so it flows agent → provider
/// without threading it through every <c>ModelRequest</c>.
/// </summary>
public static class BuildCostContext
{
    private static readonly AsyncLocal<CostScope?> Scope = new();

    public static CostScope? Current
    {
        get => Scope.Value;
        set => Scope.Value = value;
    }
}

/// <summary>Maps an agent (+ its task type) to a cost-attribution phase bucket, so the by-phase rollup is comparable.</summary>
public static class CostPhase
{
    public static string For(string? agentName, string? taskType)
    {
        if (string.Equals(agentName, AgentNames.CodeImplementer, StringComparison.Ordinal))
            return string.Equals(taskType, "CodeRepair", StringComparison.Ordinal) ? "fix-loop" : "code-gen";

        // Template-first Static build runs the cheap select+fill on one model call — same cost bucket as code-gen.
        if (string.Equals(agentName, AgentNames.StaticTemplateBuilder, StringComparison.Ordinal))
            return "code-gen";

        return agentName switch
        {
            AgentNames.QaTestEngineer => "test-impl",
            AgentNames.ProductStrategist or AgentNames.ProductOwner or AgentNames.BusinessAnalyst => "brief",
            AgentNames.Architect or AgentNames.UxAccessibilityReviewer or AgentNames.SecurityPrivacyReviewer
                or AgentNames.ContentSeoReviewer or AgentNames.DataAnalyticsReviewer
                or AgentNames.ComplianceLegalReviewer or AgentNames.ProductOwnerBranchReview => "review",
            _ => "other",
        };
    }
}
