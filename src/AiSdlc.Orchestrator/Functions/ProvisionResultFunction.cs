using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.Orchestrator.Builds;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Call-2 inbound: the provisioner POSTs the provision result here; the platform raises the
/// <c>provision-result</c> external event on the build orchestration (keyed by buildId). Authenticated by
/// the X-Provisioner-Key header against the ProvisionResultCallbackKey app setting.
/// </summary>
public sealed class ProvisionResultFunction
{
    public const string CallbackKeyHeader = "X-Provisioner-Key";
    public const string EventName = "provision-result";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<ProvisionResultFunction> _logger;

    public ProvisionResultFunction(ILogger<ProvisionResultFunction> logger) => _logger = logger;

    [Function(nameof(ProvisionResultFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "provision-result")] HttpRequestData request,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
    {
        var provided = request.Headers.TryGetValues(CallbackKeyHeader, out var vals) ? vals.FirstOrDefault() : null;
        if (!CreateBuildFunction.IsAuthorized(provided, Environment.GetEnvironmentVariable("ProvisionResultCallbackKey")))
        {
            _logger.LogWarning("provision-result rejected: bad or missing {Header}.", CallbackKeyHeader);
            return request.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var body = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
        ProvisionResult? result;
        try { result = JsonSerializer.Deserialize<ProvisionResult>(body, JsonOptions); }
        catch (JsonException ex)
        {
            _logger.LogWarning("provision-result rejected (400): malformed JSON: {Error}", ex.Message);
            return request.CreateResponse(HttpStatusCode.BadRequest);
        }

        if (result is null || string.IsNullOrWhiteSpace(result.BuildId))
            return request.CreateResponse(HttpStatusCode.BadRequest);

        await durableClient.RaiseEventAsync(result.BuildId, EventName, result, cancellation: cancellationToken);
        _logger.LogInformation("Raised {Event} on {BuildId} (outcome {Outcome}).", EventName, result.BuildId, result.Outcome);
        return request.CreateResponse(HttpStatusCode.Accepted);
    }
}
