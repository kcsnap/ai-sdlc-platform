using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;

namespace AiSdlc.Orchestrator.Builds;

/// <summary>
/// The create-build request body (Yorrixx → platform) and the input to the new-path build orchestrator.
/// Mirrors docs/roadmap/responsibility-split-phase1-e2e-schema.md. The Charter arrives via the API
/// BEFORE any repo exists; the platform derives the profile and creates the repo.
/// </summary>
public sealed record CreateBuildRequest
{
    /// <summary>Yorrixx app id — the correlation key across everything.</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>Opaque owner token; the platform needs no PII.</summary>
    public string OwnerRef { get; init; } = string.Empty;

    /// <summary>Full Charter, same shape as .yorrixx/charter.json.</summary>
    public Charter? Charter { get; init; }

    /// <summary>Base URL the platform POSTs status/runtime/verification callbacks under.</summary>
    public string CallbackBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// F9 — OPTIONAL requested generation model for every LLM call in this build (flat shorthand).
    /// Null → the platform's configured default. Validated against the allow-list at intake;
    /// after ParseAndValidate this carries the NORMALIZED value (Models.Default folds in here).
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// F9 — object form: {"default": "&lt;id&gt;", "phases": {...}}. Phases are FUTURE — parsed and
    /// ignored gracefully, never rejected. Default normalizes onto <see cref="Model"/> at intake.
    /// </summary>
    public ModelSpec? Models { get; init; }
}

/// <summary>The "models" object of the create-build request (F9). Phases are future-reserved.</summary>
public sealed record ModelSpec
{
    public string? Default { get; init; }
    public Dictionary<string, string>? Phases { get; init; }
}
