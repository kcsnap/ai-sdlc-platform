using System.Net;
using AiSdlc.Orchestrator.Builds;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Owner-signoff intake for the review gate (F1). yorrixx-app relays the owner's decision here when a
/// build sits at ready-for-review:
///   POST /api/builds/{appId}/approve          → the build proceeds to live
///   POST /api/builds/{appId}/request-changes  → the build fails with "changes requested" (body may carry
///                                               {"detail":"..."} for the owner's note)
/// Authenticated exactly like create-build: X-Platform-Build-Key vs PlatformBuildKey. The ratified phase-0
/// signoff verb (/approve-release comment) targets the old issue-driven path; API-initiated builds get this
/// first-class endpoint because their orchestration is keyed by appId, not by an issue thread.
/// </summary>
public sealed class ApproveBuildFunction
{
    public const string EventName = "owner-approval";

    private readonly ILogger<ApproveBuildFunction> _logger;

    public ApproveBuildFunction(ILogger<ApproveBuildFunction> logger) => _logger = logger;

    [Function(nameof(ApproveBuildFunction))]
    public Task<HttpResponseData> ApproveAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "builds/{appId}/approve")] HttpRequestData request,
        string appId,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
        => SignalAsync(request, appId, approved: true, durableClient, cancellationToken);

    [Function(nameof(RequestBuildChangesAsync))]
    public Task<HttpResponseData> RequestBuildChangesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "builds/{appId}/request-changes")] HttpRequestData request,
        string appId,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
        => SignalAsync(request, appId, approved: false, durableClient, cancellationToken);

    private async Task<HttpResponseData> SignalAsync(
        HttpRequestData request, string appId, bool approved,
        DurableTaskClient durableClient, CancellationToken cancellationToken)
    {
        var provided = request.Headers.TryGetValues(CreateBuildFunction.BuildKeyHeader, out var vals) ? vals.FirstOrDefault() : null;
        if (!CreateBuildFunction.IsAuthorized(provided, Environment.GetEnvironmentVariable("PlatformBuildKey")))
        {
            _logger.LogWarning("owner-approval rejected: bad or missing {Header}.", CreateBuildFunction.BuildKeyHeader);
            return request.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var buildId = CreateBuildFunction.BuildInstanceId(appId);
        var instance = await durableClient.GetInstanceAsync(buildId, cancellation: cancellationToken);
        if (instance is null || instance.IsCompleted)
        {
            _logger.LogWarning("owner-approval for {AppId}: no active build ({BuildId}).", appId, buildId);
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        var detail = await ReadDetailAsync(request, cancellationToken);
        await durableClient.RaiseEventAsync(buildId, EventName, new ApprovalSignal(approved, detail), cancellationToken);
        _logger.LogInformation("owner-approval raised on {BuildId}: approved={Approved}.", buildId, approved);

        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { buildId, approved, status = "signal-accepted" }, cancellationToken);
        return response;
    }

    private static async Task<string?> ReadDetailAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        try
        {
            var body = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // detail is best-effort; a malformed body must not block the signal
        }
    }
}
