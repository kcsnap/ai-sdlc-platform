using AiSdlc.Agents;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using System.Net;

namespace AiSdlc.Orchestrator.Functions;

public sealed class GitHubIssueWebhookFunction
{
    [Function(nameof(GitHubIssueWebhookFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "github/issues")] HttpRequestData request,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
    {
        _ = durableClient;

        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteStringAsync(
            $"GitHub issue webhook placeholder received. Future orchestration start will default to {AgentNames.ProductStrategist}.",
            cancellationToken);

        return response;
    }
}
