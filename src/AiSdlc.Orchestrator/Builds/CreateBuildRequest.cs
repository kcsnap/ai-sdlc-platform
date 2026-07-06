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
}
