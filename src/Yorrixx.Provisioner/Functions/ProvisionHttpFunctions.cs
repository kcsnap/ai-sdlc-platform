using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Yorrixx.Contracts.Hosting;
using Yorrixx.Provisioner.Contracts;
using Yorrixx.Provisioner.Internal;

namespace Yorrixx.Provisioner.Functions;

/// HTTP surface of the provisioner (routePrefix "" → /provision, /spend, … matching the platform's
/// ProvisionerClient). All long-running work is handed to the queue workers; these handlers stay fast:
/// /provision and /deprovision return 202 + enqueue, /spend is a quick Cost Management read, the rest
/// read status. Every call is gated by X-Platform-Provision-Key.
public sealed class ProvisionHttpFunctions
{
    private const string KeyHeader = "X-Platform-Provision-Key";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly TableProvisionStore _store;
    private readonly IHostingService _hosting;
    private readonly ProvisionerOptions _opts;

    public ProvisionHttpFunctions(TableProvisionStore store, IHostingService hosting, IOptions<ProvisionerOptions> opts)
    {
        _store   = store;
        _hosting = hosting;
        _opts    = opts.Value;
    }

    private bool Authorized(HttpRequestData req) =>
        !string.IsNullOrEmpty(_opts.InboundKey)
        && req.Headers.TryGetValues(KeyHeader, out var values)
        && string.Equals(values.FirstOrDefault(), _opts.InboundKey, StringComparison.Ordinal);

    [Function("health")]
    public HttpResponseData Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.WriteString("{\"status\":\"ok\"}");
        return resp;
    }

    // Call 1 — provision (async): validate, accept, enqueue, 202.
    [Function("provision")]
    public async Task<ProvisionAcceptOutput> Provision(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "provision")] HttpRequestData req)
    {
        if (!Authorized(req)) return new ProvisionAcceptOutput { Http = req.CreateResponse(HttpStatusCode.Unauthorized) };

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        ProvisionSpec? spec;
        try { spec = JsonSerializer.Deserialize<ProvisionSpec>(body, Json); }
        catch (JsonException) { return new ProvisionAcceptOutput { Http = req.CreateResponse(HttpStatusCode.BadRequest) }; }

        if (spec is null || string.IsNullOrWhiteSpace(spec.AppId) || string.IsNullOrWhiteSpace(spec.BuildId)
            || spec.Repo is null || string.IsNullOrWhiteSpace(spec.Repo.Name))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.WriteString("{\"error\":\"appId, buildId and repo are required\"}");
            return new ProvisionAcceptOutput { Http = bad };
        }

        _store.SetAccepted(spec.BuildId);
        var accepted = req.CreateResponse(HttpStatusCode.Accepted);
        await accepted.WriteAsJsonAsync(new ProvisionAccepted(spec.BuildId, "accepted"));
        return new ProvisionAcceptOutput { QueueMessage = body, Http = accepted };
    }

    // GET /provision/{buildId} — poll fallback for a dropped Call-2 callback.
    [Function("provision_get")]
    public async Task<HttpResponseData> ProvisionGet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "provision/{buildId}")] HttpRequestData req,
        string buildId)
    {
        if (!Authorized(req)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var status = _store.Get(buildId);
        if (status is null) return req.CreateResponse(HttpStatusCode.NotFound);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(status);
        return resp;
    }

    // Call 3 — deprovision (async): validate, accept, enqueue (teardown is multi-minute), 202.
    [Function("deprovision")]
    public async Task<DeprovisionAcceptOutput> Deprovision(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "deprovision")] HttpRequestData req)
    {
        if (!Authorized(req)) return new DeprovisionAcceptOutput { Http = req.CreateResponse(HttpStatusCode.Unauthorized) };

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        DeprovisionRequest? request;
        try { request = JsonSerializer.Deserialize<DeprovisionRequest>(body, Json); }
        catch (JsonException) { return new DeprovisionAcceptOutput { Http = req.CreateResponse(HttpStatusCode.BadRequest) }; }

        if (request is null || string.IsNullOrWhiteSpace(request.AppId))
            return new DeprovisionAcceptOutput { Http = req.CreateResponse(HttpStatusCode.BadRequest) };

        var accepted = req.CreateResponse(HttpStatusCode.Accepted);
        await accepted.WriteAsJsonAsync(new { status = "accepted", appId = request.AppId });
        return new DeprovisionAcceptOutput { QueueMessage = request.AppId, Http = accepted };
    }

    // Call 4 — hosting spend by appId (quick Cost Management read; degrades to 0 without RBAC).
    [Function("spend")]
    public async Task<HttpResponseData> Spend(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "spend")] HttpRequestData req)
    {
        if (!Authorized(req)) return req.CreateResponse(HttpStatusCode.Unauthorized);

        var appId = QueryValue(req.Url.Query, "appId");
        if (string.IsNullOrWhiteSpace(appId)) return req.CreateResponse(HttpStatusCode.BadRequest);

        var (minor, currency) = await _hosting.GetHostingSpendByAppIdAsync(appId, req.FunctionContext.CancellationToken);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new HostingSpend(currency, minor, DateTimeOffset.UtcNow));
        return resp;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}

/// Multi-output: the 202 HTTP response + the message enqueued for the provision worker.
public sealed class ProvisionAcceptOutput
{
    [QueueOutput("provision-queue", Connection = "AzureWebJobsStorage")]
    public string? QueueMessage { get; set; }

    public HttpResponseData Http { get; set; } = default!;
}

public sealed class DeprovisionAcceptOutput
{
    [QueueOutput("deprovision-queue", Connection = "AzureWebJobsStorage")]
    public string? QueueMessage { get; set; }

    public HttpResponseData Http { get; set; } = default!;
}
