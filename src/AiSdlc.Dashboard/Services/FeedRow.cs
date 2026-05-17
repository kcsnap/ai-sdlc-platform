namespace AiSdlc.Dashboard.Services;

// A single visual row in the activity feed. For agent events this may absorb prior retry attempts.
// For non-agent events PriorAttempts is empty.
public sealed record FeedRow(DashboardEvent Display, IReadOnlyList<DashboardEvent> PriorAttempts)
{
    public bool HasPriorAttempts => PriorAttempts.Count > 0;
    public int AttemptCount => PriorAttempts.Count + 1;
    public string Id => Display.Id;
}
