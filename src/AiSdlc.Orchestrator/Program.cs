using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.Audit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
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
    })
    .Build();

host.Run();
