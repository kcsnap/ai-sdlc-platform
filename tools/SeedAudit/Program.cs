using Azure.Data.Tables;
using Azure.Storage.Blobs;
using AiSdlc.Audit;
using AiSdlc.Shared;

var tableService = new TableServiceClient("UseDevelopmentStorage=true");
var table = tableService.GetTableClient("AuditEvents");
table.CreateIfNotExists();

var audit = new AzureTableAuditService(table);

var blobService = new BlobServiceClient("UseDevelopmentStorage=true");
var promptsContainer = blobService.GetBlobContainerClient("prompts");
promptsContainer.CreateIfNotExists();
var promptStore = new BlobPromptStore(promptsContainer);

var runId = $"launchcart_launchcart_{Random.Shared.Next(1000, 9999)}";
const string repo = "launchcart/launchcart";
var issue = Random.Shared.Next(100, 999);
var now = DateTimeOffset.UtcNow;

async Task Write(int offsetMs, string actorType, string actorName, string action, string summary,
                 string? decision = null, string? risk = null,
                 Dictionary<string, string>? references = null)
{
    await audit.WriteAsync(new AuditEvent
    {
        RunId        = runId,
        TimestampUtc = now.AddMilliseconds(offsetMs),
        Repository   = repo,
        IssueNumber  = issue,
        ActorType    = actorType,
        ActorName    = actorName,
        Action       = action,
        Summary      = summary,
        Decision     = decision,
        RiskLevel    = risk,
        References   = references ?? new Dictionary<string, string>()
    }, CancellationToken.None);
}

await promptStore.StoreAsync(runId, "BusinessAnalyst",
    "Review this spec for completeness. Spec: 'Add wishlist sharing via email'…",
    "Spec looks complete. Acceptance criteria are testable; one ambiguity around guest users — recommend explicit decision before implementation.",
    CancellationToken.None);

await promptStore.StoreAsync(runId, "SecurityPrivacyReviewer",
    "Audit the proposed wishlist sharing flow for PII/PHI exposure…",
    "Email recipient list is PII. Recommend rate-limiting share endpoint and not echoing the email back in the URL. Low severity overall.",
    CancellationToken.None);

var stackTrace = """
    System.Net.Http.HttpRequestException: Response status code does not indicate success: 429 (Too Many Requests).
       at System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode()
       at AiSdlc.ModelProviders.AnthropicModelProvider.GenerateAsync(ModelRequest request, CancellationToken cancellationToken) in /src/AiSdlc.ModelProviders/AnthropicModelProvider.cs:line 92
       at AiSdlc.Agents.Personas.ContentSeoReviewerAgent.ExecuteAsync(AgentContext context, CancellationToken ct) in /src/AiSdlc.Agents/Personas/ContentSeoReviewerAgent.cs:line 41
       at AiSdlc.Agents.AgentRunner.ExecuteAsync(AgentExecutionRequest request, CancellationToken ct) in /src/AiSdlc.Agents/AgentRunner.cs:line 28
       at AiSdlc.Orchestrator.Functions.AgentActivityFunctions.ExecuteAsync(String agentName, AgentContext context, CancellationToken cancellationToken)
    """;

await Write(   0, "Webhook", "/github/webhook",      "issues.opened",       $"Issue #{issue} opened: Add wishlist sharing via email");
await Write( 800, "System",  "Orchestrator",         "RunStarted",          "Workflow run started");
await Write(1500, "Agent",   "ProductStrategist",    "Started",             "ProductStrategist started");
await Write(2200, "Agent",   "ProductStrategist",    "Completed",           "Strategy: align with Q3 share-economy theme");
await Write(2400, "Agent",   "BusinessAnalyst",      "Started",             "BusinessAnalyst started");
await Write(2900, "Agent",   "BusinessAnalyst",      "Completed",           "Spec reviewed; one minor clarification needed", decision: "RequestClarification");
await Write(3100, "Agent",   "Architect",            "Started",             "Architect started");
await Write(3700, "Agent",   "Architect",            "Completed",           "Architecture sketch: SES + dedicated /share endpoint");
await Write(3900, "Agent",   "SecurityPrivacyReviewer","Started",           "SecurityPrivacyReviewer started");
await Write(4500, "Agent",   "SecurityPrivacyReviewer","Completed",         "PII handling acceptable with rate-limiting", risk: "Low", decision: "Approve");

// Demonstrate a failing agent — three Started + Failed pairs simulating Durable's 3 retry attempts
await Write(4600, "Agent",   "ContentSeoReviewer",   "Started",             "ContentSeoReviewer started");
await Write(4750, "Agent",   "ContentSeoReviewer",   "Failed",
    summary:    "Response status code does not indicate success: 429 (Too Many Requests).",
    references: new Dictionary<string, string>
    {
        ["exceptionType"] = "System.Net.Http.HttpRequestException",
        ["stackTrace"]    = stackTrace
    });
await Write(4900, "Agent",   "ContentSeoReviewer",   "Started",             "ContentSeoReviewer started");
await Write(5050, "Agent",   "ContentSeoReviewer",   "Failed",
    summary:    "Response status code does not indicate success: 429 (Too Many Requests).",
    references: new Dictionary<string, string>
    {
        ["exceptionType"] = "System.Net.Http.HttpRequestException",
        ["stackTrace"]    = stackTrace
    });
await Write(5200, "Agent",   "ContentSeoReviewer",   "Started",             "ContentSeoReviewer started");
await Write(5400, "Agent",   "ContentSeoReviewer",   "Completed",           "Copy adheres to brand voice; minor microcopy notes");

await Write(5500, "Agent",   "QaTestEngineer",       "Started",             "QaTestEngineer started");
await Write(6000, "Agent",   "QaTestEngineer",       "Completed",           "Test plan: 12 acceptance tests, 3 edge cases");
await Write(6300, "System",  "RiskAssessor",         "Evaluated",           "Score: 14/100. Touch zone: feature-only.", risk: "Low", decision: "AUTO_MERGE_ELIGIBLE");
await Write(6500, "Agent",   "ReleaseManager",       "Started",             "ReleaseManager started");
await Write(6800, "Agent",   "ReleaseManager",       "Completed",           "PR #142 opened (head: ai/launchcart-share-wishlist)");

Console.WriteLine($"Seeded {runId} (issue #{issue}) with full agent lifecycle including 2 transient failures + final success.");
