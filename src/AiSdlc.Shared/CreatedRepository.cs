namespace AiSdlc.Shared;

/// <summary>Coordinates of a repository created from a template.</summary>
public sealed record CreatedRepository(string FullName, string HtmlUrl, string DefaultBranch);
