using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.Contracts.Callbacks;
using AiSdlc.ModelProviders;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Cost;

/// <summary>
/// Decorates the real model provider: after each Anthropic call, POSTs raw token usage to Yorrixx for
/// per-app cost benchmarking (BuildCostCallback). Best-effort — a failed/unconfigured emit never affects
/// the build. App + iteration come from the ambient <see cref="BuildCostContext"/>; phase from the agent.
/// </summary>
public sealed class CostEmittingModelProvider : IModelProvider
{
    // Per (appId:phase:iteration) monotonic sequence for the idempotency key.
    private static readonly ConcurrentDictionary<string, int> Seq = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IModelProvider _inner;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CostEmittingModelProvider> _logger;
    private readonly string? _apiBase;
    private readonly string? _adminKey;

    public CostEmittingModelProvider(
        IModelProvider inner, IHttpClientFactory httpFactory, ILogger<CostEmittingModelProvider> logger,
        string? yorrixxApiBase, string? yorrixxAdminKey)
    {
        _inner       = inner;
        _httpFactory = httpFactory;
        _logger      = logger;
        _apiBase     = yorrixxApiBase;
        _adminKey    = yorrixxAdminKey;
    }

    public string ProviderName => _inner.ProviderName;

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var response = await _inner.CompleteAsync(request, cancellationToken);
        try
        {
            await EmitCostAsync(request, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cost telemetry emit failed (ignored — best-effort).");
        }
        return response;
    }

    private async Task EmitCostAsync(ModelRequest request, ModelResponse response, CancellationToken cancellationToken)
    {
        var scope = BuildCostContext.Current;
        if (scope is null || string.IsNullOrWhiteSpace(_apiBase) || string.IsNullOrWhiteSpace(_adminKey))
            return; // no build scope, or telemetry not configured → skip silently

        var phase  = CostPhase.For(request.AgentName, request.TaskType);
        var seq    = Seq.AddOrUpdate($"{scope.AppId}:{phase}:{scope.Iteration}", 0, (_, n) => n + 1);
        var appId8 = scope.AppId.Length > 8 ? scope.AppId[..8] : scope.AppId;

        var payload = new BuildCostCallback
        {
            Model            = response.ModelName,
            Phase            = phase,
            Iteration        = scope.Iteration,
            InputTokens      = Usage(response, "input_tokens"),
            OutputTokens     = Usage(response, "output_tokens"),
            CacheReadTokens  = Usage(response, "cache_read_tokens"),
            CacheWriteTokens = Usage(response, "cache_write_tokens"),
            Calls            = 1,
            RequestId        = $"{appId8}:{phase}:{scope.Iteration}:{seq}",
        };

        var url = $"{_apiBase!.TrimEnd('/')}/v1/admin/apps/{scope.AppId}/cost";
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: Json)
        };
        httpRequest.Headers.Add("X-Yorrixx-Admin-Key", _adminKey);

        using var resp = await http.SendAsync(httpRequest, cancellationToken);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("Cost POST for {AppId}/{Phase} returned {Status}.", scope.AppId, phase, (int)resp.StatusCode);
    }

    private static long Usage(ModelResponse response, string key) =>
        response.Usage.TryGetValue(key, out var v) && v is not null ? Convert.ToInt64(v) : 0;
}
