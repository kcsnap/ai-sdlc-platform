namespace AiSdlc.Orchestrator;

public sealed record MergePrInput(string Repository, int PullRequestNumber, string CommitMessage);
