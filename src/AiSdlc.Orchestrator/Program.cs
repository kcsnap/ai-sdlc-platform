using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.ModelProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton(new ModelProviderOptions
        {
            ProviderName    = "Anthropic",
            ModelName       = Environment.GetEnvironmentVariable("AnthropicModel") ?? "claude-haiku-4-5-20251001",
            DefaultMaxTokens = 2048
        });

        services.AddHttpClient<IModelProvider, AnthropicModelProvider>(client =>
        {
            var apiKey = Environment.GetEnvironmentVariable("AnthropicApiKey")
                ?? throw new InvalidOperationException("AnthropicApiKey is not configured.");
            client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        });

        services.AddSingleton<IAgent, ProductStrategistAgent>();
        services.AddSingleton<IAgent, ProductOwnerAgent>();
        services.AddSingleton<IAgent, BusinessAnalystAgent>();
        services.AddSingleton<IAgentRunner, AgentRunner>();

        var credential = new DefaultAzureCredential();
        var auditAccountName = Environment.GetEnvironmentVariable("AuditStorageAccountName")
            ?? throw new InvalidOperationException("AuditStorageAccountName is not configured.");

        services.AddSingleton(_ =>
        {
            var uri = new Uri($"https://{auditAccountName}.table.core.windows.net");
            return new TableClient(uri, "AuditEvents", credential);
        });

        services.AddSingleton(_ =>
        {
            var uri = new Uri($"https://{auditAccountName}.blob.core.windows.net/prompts");
            return new BlobContainerClient(uri, credential);
        });

        services.AddSingleton<IAuditService, AzureTableAuditService>();
        services.AddSingleton<IBlobPromptStore, BlobPromptStore>();

        services.AddHttpClient<IGitHubService, GitHubApiClient>(client =>
        {
            var pat = Environment.GetEnvironmentVariable("GitHubPat")
                ?? throw new InvalidOperationException("GitHubPat is not configured.");
            client.BaseAddress = new Uri("https://api.github.com");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {pat}");
            client.DefaultRequestHeaders.Add("User-Agent", "ai-sdlc-platform/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });
    })
    .Build();

host.Run();
