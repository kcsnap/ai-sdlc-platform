namespace AiSdlc.GitHub;

public interface IGitHubService
{
    Task AddIssueCommentAsync(string repository, int issueNumber, string markdown, CancellationToken cancellationToken);
    Task AddLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken);
    Task RemoveLabelsAsync(string repository, int issueOrPrNumber, IReadOnlyList<string> labels, CancellationToken cancellationToken);
}
