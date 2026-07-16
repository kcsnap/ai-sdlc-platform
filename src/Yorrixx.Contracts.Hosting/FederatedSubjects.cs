namespace Yorrixx.Contracts.Hosting;

/// <summary>
/// The two GitHub-Actions OIDC subject formats a user-app deploy identity must pin (F5).
/// GitHub progressively switched its default sub claim to the immutable form; Entra federated
/// credentials are exact-match, so both are registered per app. The immutable format is pinned
/// byte-for-byte against a live token (bikeshop probe, 2026-07-16).
/// </summary>
public static class FederatedSubjects
{
    /// <summary>Classic subject: <c>repo:{owner}/{repo}:ref:refs/heads/{branch}</c>.</summary>
    public static string Classic(string repoOwner, string repoName, string branch) =>
        $"repo:{repoOwner}/{repoName}:ref:refs/heads/{branch}";

    /// <summary>Immutable-id subject: <c>repo:{owner}@{ownerId}/{repo}@{repoId}:ref:refs/heads/{branch}</c>.</summary>
    public static string Immutable(string repoOwner, long ownerId, string repoName, long repoId, string branch) =>
        $"repo:{repoOwner}@{ownerId}/{repoName}@{repoId}:ref:refs/heads/{branch}";
}
