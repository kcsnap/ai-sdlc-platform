namespace AiSdlc.Dashboard.Services.YorrixxAdmin;

public sealed record UsersPage(IReadOnlyList<YorrixxUser> Items, string? NextCursor);
