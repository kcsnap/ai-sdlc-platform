using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.ModelProviders;
using AiSdlc.Orchestrator.Imagery;
using AiSdlc.Orchestrator.Webhooks;
using AiSdlc.RepoIndex;
using AiSdlc.Shared.AutoMerge;
using AiSdlc.Shared.Redaction;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    // Application Insights is enabled automatically when APPLICATIONINSIGHTS_CONNECTION_STRING
    // is set. The Microsoft.Azure.Functions.Worker.ApplicationInsights package wires it up.
    .ConfigureServices(services =>
    {
        services.AddSingleton(new ModelProviderOptions
        {
            ProviderName    = "Anthropic",
            ModelName       = Environment.GetEnvironmentVariable("AnthropicModel") ?? "claude-haiku-4-5-20251001",
            DefaultMaxTokens = 2048,
            // Optional per-agent override, e.g. run the design-critical steps on a stronger model:
            //   AnthropicModelOverrides="Code Implementer=claude-opus-4-8;UX / Accessibility Reviewer=claude-opus-4-8"
            // Unset → every agent uses the global model above (no behaviour change).
            ModelOverridesByAgent = ModelProviderOptions.ParseOverrides(
                Environment.GetEnvironmentVariable("AnthropicModelOverrides"))
        });

        services.AddSingleton<IRedactionService, RegexRedactionService>();

        // Shared across all agents so parallel fan-outs draw from one budget view —
        // keeps the platform inside its Anthropic usage-tier limits instead of 429ing.
        services.AddSingleton(new AnthropicRateLimiterOptions
        {
            MaxConcurrentRequests =
                int.TryParse(Environment.GetEnvironmentVariable("AnthropicMaxConcurrentRequests"), out var maxConcurrent) && maxConcurrent > 0
                    ? maxConcurrent
                    : 2
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AnthropicRateLimiter>();

        services.AddHttpClient<IModelProvider, AnthropicModelProvider>(client =>
        {
            var apiKey = Environment.GetEnvironmentVariable("AnthropicApiKey")
                ?? throw new InvalidOperationException("AnthropicApiKey is not configured.");
            client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            // The default HttpClient timeout is 100s — too short for a large generation (a Code
            // Implementer batch on Opus emitting up to 16k tokens routinely exceeds it, surfacing as
            // TaskCanceledException and failing the stage). Match ThemeHarness's 5-minute ceiling.
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // Real photography for marketing pages — used server-side only (the page gets public image URLs,
        // never the key). Without PexelsApiKey the platform stays generative-only (safe, inert default).
        var pexelsApiKey = Environment.GetEnvironmentVariable("PexelsApiKey");
        if (string.IsNullOrWhiteSpace(pexelsApiKey))
        {
            services.AddSingleton<IImageSource, NoOpImageSource>();
        }
        else
        {
            services.AddHttpClient<IImageSource, PexelsImageSource>(client =>
            {
                client.BaseAddress = new Uri("https://api.pexels.com/v1/");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(pexelsApiKey);
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        }

        services.AddSingleton<IAgent, ProductStrategistAgent>();
        services.AddSingleton<IAgent, ProductOwnerAgent>();
        services.AddSingleton<IAgent, BusinessAnalystAgent>();
        services.AddSingleton<IAgent, CodeImplementerAgent>();
        services.AddSingleton<IAgent, ArchitectAgent>();
        services.AddSingleton<IAgent, UxAccessibilityReviewerAgent>();
        services.AddSingleton<IAgent, ContentSeoReviewerAgent>();
        services.AddSingleton<IAgent, DataAnalyticsReviewerAgent>();
        services.AddSingleton<IAgent, ComplianceLegalReviewerAgent>();
        services.AddSingleton<IAgent, QaTestEngineerAgent>();
        services.AddSingleton<IAgent, SeniorCoderAgent>();
        services.AddSingleton<IAgent, SecurityPrivacyReviewerAgent>();
        services.AddSingleton<IAgent, DevOpsPlatformEngineerAgent>();
        services.AddSingleton<IAgent, ProductOwnerBranchReviewAgent>();
        services.AddSingleton<IAgent, RiskAssessorAgent>();
        services.AddSingleton<IAgent, ReleaseManagerAgent>();
        services.AddSingleton<IAgentRunner, AgentRunner>();
        services.AddSingleton<IAutoMergeEligibilityService, AutoMergeEligibilityService>();

        var credential = new DefaultAzureCredential();
        var auditAccountName = Environment.GetEnvironmentVariable("AuditStorageAccountName")
            ?? throw new InvalidOperationException("AuditStorageAccountName is not configured.");

        services.AddSingleton(_ =>
        {
            var uri = new Uri($"https://{auditAccountName}.table.core.windows.net");
            return new TableClient(uri, "AuditEvents", credential);
        });

        services.AddSingleton<IAuditService, AzureTableAuditService>();

        services.AddSingleton<IBlobPromptStore>(_ =>
            new BlobPromptStore(new BlobContainerClient(
                new Uri($"https://{auditAccountName}.blob.core.windows.net/prompts"), credential)));

        services.AddSingleton<IContextStore>(_ =>
            new BlobContextStore(new BlobContainerClient(
                new Uri($"https://{auditAccountName}.blob.core.windows.net/context"), credential)));

        services.AddTransient<GitHubTransientRetryHandler>();
        services.AddHttpClient<IGitHubService, GitHubApiClient>(client =>
        {
            var pat = Environment.GetEnvironmentVariable("GitHubPat")
                ?? throw new InvalidOperationException("GitHubPat is not configured.");
            client.BaseAddress = new Uri("https://api.github.com");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {pat}");
            client.DefaultRequestHeaders.Add("User-Agent", "ai-sdlc-platform/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }).AddHttpMessageHandler<GitHubTransientRetryHandler>();

        services.AddSingleton<IRepoIndexer, GitHubRepoIndexer>();
        services.AddSingleton<AiSdlc.RepoIndex.Charter.ICharterReader, AiSdlc.RepoIndex.Charter.GitHubCharterReader>();

        // Fast-ACK webhook intake: queue + overflow blob live on the host storage account
        // (the managed identity already holds queue/blob data roles there for Durable Functions).
        // Locally (Azurite) AzureWebJobsStorage__accountName is absent — fall back to the
        // development-storage connection string.
        services.AddSingleton<GitHubWebhookProcessor>();
        services.AddSingleton<IWebhookInbox>(_ =>
        {
            var hostAccount = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName");
            var queueOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

            if (hostAccount is not null)
            {
                return new StorageQueueWebhookInbox(
                    new QueueClient(
                        new Uri($"https://{hostAccount}.queue.core.windows.net/{StorageQueueWebhookInbox.QueueName}"),
                        credential, queueOptions),
                    new BlobContainerClient(
                        new Uri($"https://{hostAccount}.blob.core.windows.net/{StorageQueueWebhookInbox.OverflowContainerName}"),
                        credential));
            }

            var devConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
            return new StorageQueueWebhookInbox(
                new QueueClient(devConnection, StorageQueueWebhookInbox.QueueName, queueOptions),
                new BlobContainerClient(devConnection, StorageQueueWebhookInbox.OverflowContainerName));
        });
    })
    .Build();

host.Run();
