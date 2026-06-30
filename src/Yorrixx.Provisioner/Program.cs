using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yorrixx.Modules.Hosting;
using Yorrixx.Provisioner.Internal;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // Hosting module — ArmClient + IHostingService (+ deploy identity + Clerk + IHttpClientFactory).
        // Real impls when Hosting:SubscriptionId is set; stubs otherwise so the host boots without the
        // high-privilege grants. The provisioner's dedicated UAMI is selected by DefaultAzureCredential
        // via the AZURE_CLIENT_ID app setting.
        new HostingModule().Register(services, config);

        services.Configure<ProvisionWorkerOptions>(config.GetSection(ProvisionWorkerOptions.SectionName));
        services.Configure<PlatformCallbackOptions>(config.GetSection(PlatformCallbackOptions.SectionName));
        services.Configure<ProvisionerOptions>(config.GetSection(ProvisionerOptions.SectionName));
        services.AddHttpClient<PlatformCallbackClient>();

        // Provision-status table (identity-based, on the function app's own storage account) so the GET
        // /provision/{buildId} poll fallback survives restarts. The work handoff itself rides the
        // AzureWebJobsStorage queues via the function bindings.
        var storageAccount = config["Provisioner:StorageAccountName"] ?? string.Empty;
        services.AddSingleton(_ =>
        {
            var table = new TableClient(
                new Uri($"https://{storageAccount}.table.core.windows.net"),
                TableProvisionStore.TableName,
                new DefaultAzureCredential());
            return new TableProvisionStore(table);
        });
    })
    .Build();

host.Run();
