namespace AiSdlc.GitHub;

/// <summary>One result from an org-wide issue search (see IGitHubService.SearchOpenOrgIssuesByLabelAsync).</summary>
public sealed record OrgIssueSearchHit(
    string Repository,
    int Number,
    string Title,
    string? Body,
    string Url,
    string Author,
    IReadOnlyList<string> Labels,
    DateTimeOffset UpdatedAt);
