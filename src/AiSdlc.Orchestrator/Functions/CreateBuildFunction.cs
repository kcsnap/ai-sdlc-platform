using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.Orchestrator.Builds;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Build-intake API (Phase-1 new path). Yorrixx POSTs a Charter here to start a build; the platform
/// derives the profile, creates the repo, provisions, builds, verifies, and reports back via callbacks.
/// The Charter arrives via the API BEFORE any repo exists. Idempotent on appId (re-submit == retry):
/// an in-flight build returns the same buildId; a terminal one is purged and restarted.
/// </summary>
public sealed class CreateBuildFunction
{
    public const string BuildKeyHeader = "X-Platform-Build-Key";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<CreateBuildFunction> _logger;

    public CreateBuildFunction(ILogger<CreateBuildFunction> logger) => _logger = logger;

    [Function(nameof(CreateBuildFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "builds")] HttpRequestData request,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(HeaderOrNull(request, BuildKeyHeader), Environment.GetEnvironmentVariable("PlatformBuildKey")))
        {
            _logger.LogWarning("create-build rejected: bad or missing {Header}.", BuildKeyHeader);
            return request.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var body = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
        var (buildRequest, error) = ParseAndValidate(body);
        if (buildRequest is null)
        {
            _logger.LogWarning("create-build rejected (400): {Error}", error);
            var bad = request.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error }, cancellationToken);
            return bad;
        }

        var buildId = BuildInstanceId(buildRequest.AppId);

        // Idempotent on appId: a re-submit while a build is in flight returns the same buildId; a terminal
        // prior build is purged and restarted (mirrors the issue-webhook restart path).
        var existing = await durableClient.GetInstanceAsync(buildId, cancellation: cancellationToken);
        if (existing is not null && !existing.IsCompleted)
        {
            _logger.LogInformation("create-build for {AppId} already in flight ({BuildId}).", buildRequest.AppId, buildId);
        }
        else
        {
            if (existing is not null)
                await durableClient.PurgeInstanceAsync(buildId, cancellation: cancellationToken);

            try
            {
                await durableClient.ScheduleNewOrchestrationInstanceAsync(
                    nameof(NewAppBuildOrchestrator),
                    buildRequest,
                    new StartOrchestrationOptions { InstanceId = buildId },
                    cancellationToken);
                _logger.LogInformation("Started build {BuildId} for app {AppId}.", buildId, buildRequest.AppId);
            }
            catch (Exception ex)
            {
                // Concurrent-duplicate race (yorrixx's timeout-retry can land while the original request
                // is still scheduling): both readers saw no instance, both scheduled, the loser throws.
                // If an instance exists now, the build IS running — same-buildId ack is the correct
                // idempotent answer. Anything else is a genuine failure.
                var raced = await durableClient.GetInstanceAsync(buildId, cancellation: cancellationToken);
                if (raced is null)
                    throw;
                _logger.LogInformation(ex,
                    "create-build for {AppId} raced a concurrent duplicate ({BuildId}) — instance exists, treating as accepted.",
                    buildRequest.AppId, buildId);
            }
        }

        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { buildId, status = "accepted" }, cancellationToken);
        return response;
    }

    internal static string BuildInstanceId(string appId) => $"build-{appId}";

    internal static bool IsAuthorized(string? provided, string? configured)
    {
        // Not configured (local/dev) — mirror the webhook's skip-when-unset behaviour.
        if (string.IsNullOrEmpty(configured)) return true;
        if (string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(configured));
    }

    internal static (CreateBuildRequest? request, string? error) ParseAndValidate(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, "empty request body");

        CreateBuildRequest? req;
        try { req = JsonSerializer.Deserialize<CreateBuildRequest>(body, JsonOptions); }
        catch (JsonException ex) { return (null, $"malformed JSON: {ex.Message}"); }

        if (req is null) return (null, "null request");
        if (string.IsNullOrWhiteSpace(req.AppId)) return (null, "appId is required");
        if (string.IsNullOrWhiteSpace(req.CallbackBaseUrl)) return (null, "callbackBaseUrl is required");
        if (!Uri.TryCreate(req.CallbackBaseUrl, UriKind.Absolute, out _)) return (null, "callbackBaseUrl must be an absolute URL");
        if (req.Charter is null) return (null, "charter is required");
        if (string.IsNullOrWhiteSpace(req.Charter.Identity.AppName)) return (null, "charter.Identity.AppName is required");

        return (req, null);
    }

    private static string? HeaderOrNull(HttpRequestData request, string name) =>
        request.Headers.TryGetValues(name, out var vals) ? vals.FirstOrDefault() : null;
}
