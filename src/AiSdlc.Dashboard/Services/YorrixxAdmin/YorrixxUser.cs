namespace AiSdlc.Dashboard.Services.YorrixxAdmin;

public sealed record YorrixxUser(
    string Email,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt,
    int ApplicationCount,
    int ApplicationQuota,
    string? Status);
