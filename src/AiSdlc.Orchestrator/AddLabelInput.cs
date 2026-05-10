namespace AiSdlc.Orchestrator;

public sealed record AddLabelInput(string Repository, int IssueOrPrNumber, string Label);
