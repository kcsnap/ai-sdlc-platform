namespace AiSdlc.GitHub;

/// <summary>An open PR a new run may resume from (head branch + current head sha).</summary>
public sealed record OpenPullRequestInfo(int Number, string HeadBranch, string HeadSha);
