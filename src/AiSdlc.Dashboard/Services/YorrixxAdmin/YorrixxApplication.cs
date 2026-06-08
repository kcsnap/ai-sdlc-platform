namespace AiSdlc.Dashboard.Services.YorrixxAdmin;

public sealed record YorrixxApplication(
    string Id,
    string DisplayName,
    DateTimeOffset CreatedAt,
    string Status,
    string? PublicUrl,
    string? RepoUrl);
