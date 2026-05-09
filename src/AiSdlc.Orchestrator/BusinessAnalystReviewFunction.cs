using AiSdlc.Agents;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace AiSdlc.Orchestrator;

public sealed class BusinessAnalystReviewFunction
{
    private readonly IAgentRunner _agentRunner;

    public BusinessAnalystReviewFunction(IAgentRunner agentRunner)
    {
        _agentRunner = agentRunner;
    }

    [Function(nameof(BusinessAnalystReviewFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "agents/business-analyst/review")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await JsonSerializer.DeserializeAsync<BusinessAnalystReviewRequest>(
            request.Body,
            cancellationToken: cancellationToken);

        if (payload is null || string.IsNullOrWhiteSpace(payload.SpecMarkdown))
        {
            var badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A JSON body with at least 'specMarkdown' is required.", cancellationToken);
            return badRequest;
        }

        var executionResult = await _agentRunner.ExecuteAsync(
            new AgentExecutionRequest
            {
                AgentName = AgentNames.BusinessAnalyst,
                Context = new AgentContext
                {
                    RunId = payload.RunId ?? Guid.NewGuid().ToString("N"),
                    Repository = payload.Repository ?? "unknown/repository",
                    IssueNumber = payload.IssueNumber,
                    CurrentState = WorkflowRunStatus.Analysing.ToString(),
                    RequestedAgent = AgentNames.BusinessAnalyst,
                    Metadata = new Dictionary<string, object>
                    {
                        ["specTitle"] = payload.SpecTitle ?? string.Empty,
                        ["specMarkdown"] = payload.SpecMarkdown,
                        ["existingProductContext"] = payload.ExistingProductContext ?? string.Empty
                    }
                }
            },
            cancellationToken);

        if (!executionResult.Succeeded || executionResult.Result is null)
        {
            var errorResponse = request.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(executionResult.ErrorMessage ?? "Business Analyst execution failed.", cancellationToken);
            return errorResponse;
        }

        var okResponse = request.CreateResponse(HttpStatusCode.OK);
        await okResponse.WriteAsJsonAsync(
            new BusinessAnalystReviewResponse
            {
                Status = executionResult.Result.Status,
                Summary = executionResult.Result.Summary,
                OutputMarkdown = executionResult.Result.OutputMarkdown ?? string.Empty,
                FollowUpQuestions = executionResult.Result.FollowUpQuestions,
                BlockingIssues = executionResult.Result.BlockingIssues
            },
            cancellationToken);

        return okResponse;
    }

    public sealed record BusinessAnalystReviewRequest
    {
        public string? RunId { get; init; }
        public string? Repository { get; init; }
        public int IssueNumber { get; init; }
        public string? SpecTitle { get; init; }
        public required string SpecMarkdown { get; init; }
        public string? ExistingProductContext { get; init; }
    }

    public sealed record BusinessAnalystReviewResponse
    {
        public required string Status { get; init; }
        public required string Summary { get; init; }
        public required string OutputMarkdown { get; init; }
        public IReadOnlyList<string> FollowUpQuestions { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> BlockingIssues { get; init; } = Array.Empty<string>();
    }
}
