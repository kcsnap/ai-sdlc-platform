using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AiSdlc.Dashboard.Services.YorrixxAdmin;

public sealed class YorrixxAdminClient : IYorrixxAdminClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public YorrixxAdminClient(HttpClient http) => _http = http;

    public async Task<UsersPage> ListUsersAsync(string? query, string? cursor, int pageSize, CancellationToken ct)
    {
        var url = $"v1/admin/users?pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(query))
        {
            url += $"&q={Uri.EscapeDataString(query)}";
        }
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        var page = await _http.GetFromJsonAsync<UsersPage>(url, JsonOptions, ct);
        return page ?? new UsersPage(Array.Empty<YorrixxUser>(), null);
    }

    // Yorrixx admin API doesn't yet expose GET /v1/admin/users/{email}, so we fetch the
    // single user via the list endpoint's q= filter and verify an exact email match.
    public async Task<YorrixxUser?> GetUserAsync(string email, CancellationToken ct)
    {
        var page = await ListUsersAsync(query: email, cursor: null, pageSize: 10, ct);
        return page.Items.FirstOrDefault(u =>
            string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<YorrixxApplication>> GetUserApplicationsAsync(string email, CancellationToken ct)
    {
        var url = $"v1/admin/users/{Uri.EscapeDataString(email)}/applications";

        using var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<YorrixxApplication>();
        }
        response.EnsureSuccessStatusCode();

        var apps = await response.Content.ReadFromJsonAsync<List<YorrixxApplication>>(JsonOptions, ct);
        return apps ?? new List<YorrixxApplication>();
    }

    public async Task<YorrixxUser> UpdateUserQuotaAsync(string email, int applicationQuota, CancellationToken ct)
    {
        var url = $"v1/admin/users/{Uri.EscapeDataString(email)}";
        var body = new { applicationQuota };

        using var response = await _http.PatchAsJsonAsync(url, body, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await TryReadProblemDetailAsync(response, ct);
            var status = (int)response.StatusCode;
            throw new HttpRequestException(
                detail ?? $"Update failed: {status} {response.ReasonPhrase}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var updated = await response.Content.ReadFromJsonAsync<YorrixxUser>(JsonOptions, ct);
        return updated ?? throw new InvalidOperationException("PATCH returned empty body.");
    }

    private static async Task<string?> TryReadProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, ct);
            return problem?.Detail ?? problem?.Title;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ProblemDetails(string? Type, string? Title, int? Status, string? Detail, string? Instance);
}
