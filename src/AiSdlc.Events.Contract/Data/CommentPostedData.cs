namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Orchestrator posted a markdown comment to the GitHub issue or pull request.
/// </summary>
/// <param name="Summary">Heading extracted from the comment body (truncated to 256 chars at source).</param>
/// <param name="CommentUrl">Direct URL to the posted comment.</param>
/// <param name="CommentId">GitHub comment ID — useful for de-duplication and direct API navigation.</param>
public sealed record CommentPostedData(
    string Summary,
    string CommentUrl,
    long CommentId) : EventData;
