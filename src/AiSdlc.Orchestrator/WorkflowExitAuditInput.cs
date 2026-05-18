namespace AiSdlc.Orchestrator;

// Input to AgentActivityFunctions.RecordWorkflowExitAsync. Outcome is "Stopped" or "Failed";
// Reason is a short human-readable string surfaced in the dashboard's audit feed.
public sealed record WorkflowExitAuditInput(
    string Repository,
    int IssueNumber,
    string Outcome,
    string Reason);
