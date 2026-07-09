namespace AiSdlc.Orchestrator.Builds;

/// <summary>
/// Owner-signoff signal for the review gate (F1): raised as the <c>owner-approval</c> external event on
/// the build orchestration when yorrixx-app relays the owner's Approve / Request-changes click.
/// </summary>
public sealed record ApprovalSignal(bool Approved, string? Detail = null);
