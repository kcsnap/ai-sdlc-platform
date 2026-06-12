namespace AiSdlc.Orchestrator;

/// <summary>Snapshot of a commit's check runs for the pre-merge gate (#88).</summary>
public sealed record ChecksState(int Total, int Pending, List<string> FailedNames);

/// <summary>Input to AgentActivityFunctions.FetchReopenFindingsAsync.</summary>
public sealed record FetchReopenFindingsInput(string Repository, int IssueNumber);

/// <summary>
/// Input to AgentActivityFunctions.FetchExistingSourceAsync. Branch is nullable for
/// back-compat with the reopen path (null → the repo's default branch).
/// </summary>
public sealed record FetchExistingSourceInput(string RunId, string Repository, string? Branch = null);

/// <summary>Input to AgentActivityFunctions.FetchCiFailureFindingsAsync. Attempt feeds the blob key.</summary>
public sealed record FetchCiFindingsInput(string RunId, string Repository, string HeadSha, int Attempt);
