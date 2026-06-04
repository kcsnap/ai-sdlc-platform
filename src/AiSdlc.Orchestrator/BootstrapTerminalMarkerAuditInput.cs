namespace AiSdlc.Orchestrator;

// Input to AgentActivityFunctions.EmitBootstrapTerminalMarkerAuditAsync. Emits the typed audit
// counterpart of the HTML-comment terminal marker from PR #51. Status is "completed" or "failed".
// See ADR-0004 § "Terminal markers relationship".
public sealed record BootstrapTerminalMarkerAuditInput(
    string Repository,
    int IssueNumber,
    string Status);
