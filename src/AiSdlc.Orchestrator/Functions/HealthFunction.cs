using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Real worker-executed health probe. The Functions default-hostname root ("/") is served by the
/// platform front end WITHOUT specializing a worker, so probing it proves nothing about warmth —
/// that mirage masked the G6 flip-#2 cold intake. This endpoint (a) gives monitoring a truthful
/// signal and (b) is the target of the App Insights availability ping that keeps the http trigger
/// group specialized on Flex Consumption, where alwaysReady is a soft target the platform can
/// deallocate (observed 2026-07-05: >100s cold start despite alwaysReady http=1).
/// </summary>
public sealed class HealthFunction
{
    [Function(nameof(HealthFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "ok" });
        return response;
    }
}
