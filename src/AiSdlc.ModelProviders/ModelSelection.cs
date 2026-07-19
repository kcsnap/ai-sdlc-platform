using System.Threading;

namespace AiSdlc.ModelProviders;

/// <summary>
/// Ambient per-build model selection (F9): set by the agent-run activity from the build request's
/// requested model, read by <see cref="AnthropicModelProvider"/> at call time. AsyncLocal so it flows
/// agent → provider without threading a parameter through every persona (mirrors BuildCostContext).
/// The requested model wins over BOTH the global default and any per-agent env override — it is the
/// owner's explicit choice for this build.
/// </summary>
public static class ModelSelectionContext
{
    private static readonly AsyncLocal<string?> Requested = new();

    public static string? RequestedModel
    {
        get => Requested.Value;
        set => Requested.Value = value;
    }
}

/// <summary>
/// The model's safety classifiers declined to generate (stop_reason "refusal", e.g. claude-fable-5).
/// Deterministic for a given prompt+model — callers must treat it as a build failure with a clear
/// message, never retry it.
/// </summary>
public sealed class ModelRefusalException : Exception
{
    public ModelRefusalException(string message) : base(message) { }
}
