
namespace AiSdlc.RepoIndex.Charter;

// Inside this namespace the simple name "Charter" binds to the namespace itself, so the contract-package
// import must live INSIDE the namespace body (inner-scope usings win the lookup).
using Yorrixx.Contracts.Generation;

public interface ICharterReader
{
    /// <summary>
    /// Reads .yorrixx/charter.json from the default branch of <paramref name="repository"/>.
    /// Returns null when the file is missing, malformed, or carries an unrecognised SchemaVersion.
    /// </summary>
    Task<Charter?> ReadAsync(string repository, CancellationToken cancellationToken);
}
