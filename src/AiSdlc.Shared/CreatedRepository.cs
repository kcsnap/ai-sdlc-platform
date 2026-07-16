namespace AiSdlc.Shared;

/// <summary>Coordinates of a repository created from a template.</summary>
// OwnerId/RepoId are GitHub's immutable numeric ids — required for the immutable OIDC subject
// (F5); defaults keep pre-F5 constructions (tests, stubs) compiling.
public sealed record CreatedRepository(string FullName, string HtmlUrl, string DefaultBranch, long OwnerId = 0, long RepoId = 0);
