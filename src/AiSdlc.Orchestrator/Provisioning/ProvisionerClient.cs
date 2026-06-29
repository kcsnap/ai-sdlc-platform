using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yorrixx.Provisioner.Contracts;

namespace AiSdlc.Orchestrator.Provisioning;

/// <summary>Client for the extracted provisioner (Yorrixx.Provisioner) — Call 1 + the GET poll fallback.</summary>
public interface IProvisionerClient
{
    /// <summary>Call 1 — fire the async provision request (provisioner 202s; result arrives via callback).</summary>
    Task StartProvisionAsync(ProvisionSpec request, CancellationToken cancellationToken);

    /// <summary>Poll fallback — GET /provision/{buildId}; null when no result is available yet.</summary>
    Task<ProvisionResult?> GetProvisionResultAsync(string buildId, CancellationToken cancellationToken);
}

public sealed class ProvisionerClient : IProvisionerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public ProvisionerClient(HttpClient http) => _http = http;

    public async Task StartProvisionAsync(ProvisionSpec request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("provision", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ProvisionResult?> GetProvisionResultAsync(string buildId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"provision/{buildId}", cancellationToken);
        // Not ready yet (still provisioning) → no result to act on.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Accepted or HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProvisionResult>(JsonOptions, cancellationToken);
    }
}

/// <summary>Inert client when no provisioner is configured (local/dev) — throws if actually used.</summary>
public sealed class NoOpProvisionerClient : IProvisionerClient
{
    private static InvalidOperationException NotConfigured() =>
        new("Provisioner is not configured — set the ProvisionerUrl app setting to enable provisioning.");

    public Task StartProvisionAsync(ProvisionSpec request, CancellationToken cancellationToken) => throw NotConfigured();
    public Task<ProvisionResult?> GetProvisionResultAsync(string buildId, CancellationToken cancellationToken) => throw NotConfigured();
}
