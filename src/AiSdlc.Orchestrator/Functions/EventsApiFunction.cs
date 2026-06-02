using System.Net;
using System.Text.Json;
using AiSdlc.Audit;
using AiSdlc.Events.Contract;
using AiSdlc.Orchestrator.Events;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// HTTP endpoint implementing the per-run event-stream contract from ADR-0004.
/// Path: <c>GET /v1/runs/{runId}/events?since={cursor}&amp;limit={n}</c>.
/// </summary>
public sealed class EventsApiFunction
{
    internal const int DefaultLimit = 100;
    internal const int MaxLimit = 500;

    private readonly IAuditService _audit;
    private readonly ILogger<EventsApiFunction> _logger;

    public EventsApiFunction(IAuditService audit, ILogger<EventsApiFunction> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    [Function(nameof(EventsApiFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/runs/{runId}/events")] HttpRequestData request,
        string runId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return await BadRequestAsync(request, "runId is required.", cancellationToken);
        }

        var query = ParseQueryString(request.Url.Query);
        var sinceCursor = query.GetValueOrDefault("since");
        var limit = ParseLimit(query.GetValueOrDefault("limit"));

        if (!string.IsNullOrEmpty(sinceCursor) && !CursorCodec.TryDecode(sinceCursor, out _))
        {
            return await BadRequestAsync(request, "Malformed 'since' cursor.", cancellationToken);
        }

        var response = await BuildResponseAsync(runId, sinceCursor, limit, cancellationToken);

        _logger.LogInformation(
            "EventsApi served {Count} events for run {RunId} (hasMore={HasMore})",
            response.Events.Count, runId, response.HasMore);

        return await WriteJsonAsync(request, HttpStatusCode.OK, response, cancellationToken);
    }

    internal async Task<EventsResponse> BuildResponseAsync(
        string runId, string? sinceCursor, int limit, CancellationToken cancellationToken)
    {
        string? rowKeyExclusive = null;
        if (!string.IsNullOrEmpty(sinceCursor) && CursorCodec.TryDecode(sinceCursor, out var decoded))
        {
            rowKeyExclusive = decoded;
        }

        // Fetch limit+1 so we can detect hasMore without an extra round trip.
        var fetched = await _audit.GetByRunIdAfterRowKeyAsync(
            runId, rowKeyExclusive, limit + 1, cancellationToken);

        var hasMore = fetched.Count > limit;
        var page = hasMore ? fetched.Take(limit).ToList() : fetched.ToList();

        var envelopes = page
            .Select(AuditEventMapper.TryMap)
            .Where(envelope => envelope is not null)
            .Select(envelope => envelope!)
            .ToList();

        var nextCursor = page.Count > 0
            ? CursorCodec.Encode(page[^1].RowKey)
            : sinceCursor ?? string.Empty;

        return new EventsResponse(envelopes, nextCursor, hasMore);
    }

    internal static int ParseLimit(string? limitString)
    {
        if (string.IsNullOrEmpty(limitString) || !int.TryParse(limitString, out var limit))
        {
            return DefaultLimit;
        }

        return Math.Clamp(limit, 1, MaxLimit);
    }

    internal static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }

        return result;
    }

    private static async Task<HttpResponseData> BadRequestAsync(
        HttpRequestData request, string message, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message, cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request, HttpStatusCode statusCode, T payload, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var json = JsonSerializer.Serialize(payload, EventStreamSerializer.Options);
        await response.WriteStringAsync(json, cancellationToken);
        return response;
    }
}
