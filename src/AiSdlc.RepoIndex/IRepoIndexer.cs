namespace AiSdlc.RepoIndex;

public interface IRepoIndexer
{
    /// <summary>
    /// Reads .ai-sdlc.yml from <paramref name="repository"/> and returns a structured index.
    /// Returns null if the repo has no .ai-sdlc.yml.
    /// </summary>
    Task<RepoIndex?> IndexAsync(string repository, CancellationToken cancellationToken);
}
