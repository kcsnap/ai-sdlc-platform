namespace AiSdlc.RepoIndex.Charter;

public interface ICharterReader
{
    /// <summary>
    /// Reads .yorrixx/charter.json from the default branch of <paramref name="repository"/>.
    /// Returns null when the file is missing, malformed, or carries an unrecognised SchemaVersion.
    /// </summary>
    Task<Charter?> ReadAsync(string repository, CancellationToken cancellationToken);
}
