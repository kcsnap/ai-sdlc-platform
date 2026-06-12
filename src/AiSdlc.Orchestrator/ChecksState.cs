namespace AiSdlc.Orchestrator;

/// <summary>Snapshot of a commit's check runs for the pre-merge gate (#88).</summary>
public sealed record ChecksState(int Total, int Pending, List<string> FailedNames);

/// <summary>Input to AgentActivityFunctions.FetchReopenFindingsAsync.</summary>
public sealed record FetchReopenFindingsInput(string Repository, int IssueNumber);

/// <summary>Input to AgentActivityFunctions.FetchExistingSourceAsync.</summary>
public sealed record FetchExistingSourceInput(string RunId, string Repository);
