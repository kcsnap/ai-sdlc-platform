namespace AiSdlc.Dashboard.Services.YorrixxAdmin;

public interface IYorrixxAdminClient
{
    Task<UsersPage> ListUsersAsync(string? query, string? cursor, int pageSize, CancellationToken ct);

    Task<YorrixxUser?> GetUserAsync(string email, CancellationToken ct);

    Task<IReadOnlyList<YorrixxApplication>> GetUserApplicationsAsync(string email, CancellationToken ct);

    Task<YorrixxUser> UpdateUserQuotaAsync(string email, int applicationQuota, CancellationToken ct);
}
