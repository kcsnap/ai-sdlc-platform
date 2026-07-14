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

        // Cost net (D3 was the THIRD silently-lost-cost defect): an unconfigured emitter must be loud
        // at boot, not discovered months later from an empty benchmark (YorrixxApiBase sat unset from
        // #185 until w1proof2).
        if (string.IsNullOrWhiteSpace(_apiBase) || string.IsNullOrWhiteSpace(_adminKey))
            _logger.LogWarning("Cost telemetry is INERT — YorrixxApiBase and/or YorrixxAdminKey is not configured; no build will emit cost.");
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
        if (string.IsNullOrWhiteSpace(_apiBase) || string.IsNullOrWhiteSpace(_adminKey))
            return; // not configured — already warned loudly at construction

        var scope = BuildCostContext.Current;
        if (scope is null)
        {
            // Cost net: a real LLM call with no attribution scope means an orchestration path reached the
            // model provider without setting BuildCostContext — that cost is LOST. Make it loud.
            _logger.LogWarning("LLM call by {Agent}/{TaskType} carried no BuildCostContext — cost NOT attributed (unwired orchestration path).",
                request.AgentName, request.TaskType);
            return;
        }

        var phase  = CostPhase.For(request.AgentName, request.TaskType);
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
            // D3: the key was {appId8}:{phase}:{iteration}:{seq} with seq from an IN-PROCESS counter —
            // a re-kicked build in a fresh worker regenerated the previous run's exact RequestIds and
            // Yorrixx's idempotency dedupe silently swallowed all 21 emits ($0.00 delta on 13 min of
            // repair work). Each emit is one real, billed Anthropic call and the POST is never retried,
            // so cross-call dedupe protects nothing: the key must be unique per emit. The readable
            // prefix stays for observability; the GUID guarantees uniqueness across runs and restarts.
            RequestId        = $"{appId8}:{phase}:{scope.Iteration}:{Guid.NewGuid():N}",
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
