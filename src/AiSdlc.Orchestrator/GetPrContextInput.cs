namespace AiSdlc.Orchestrator;

public sealed record GetPrContextInput(string Repository, int PullRequestNumber, string HeadSha);
