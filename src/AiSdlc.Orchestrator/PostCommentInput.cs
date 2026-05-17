namespace AiSdlc.Orchestrator;

public sealed record PostCommentInput(
    string Repository,
    int IssueNumber,
    string Markdown,
    IReadOnlyDictionary<string, string>? ContentRefs = null);
