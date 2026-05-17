using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using AiSdlc.Audit;
using AiSdlc.Dashboard.Components;
using AiSdlc.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<DashboardOptions>(
    builder.Configuration.GetSection(DashboardOptions.SectionName));

builder.Services.AddSingleton<DashboardEventBus>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DashboardOptions>>().Value;
    return new DashboardEventBus(opts.MaxEventsInMemory);
});

builder.Services.AddSingleton<IAuditService>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DashboardOptions>>().Value;
    var client = CreateAuditTableClient(opts);
    return new AzureTableAuditService(client);
});

builder.Services.AddSingleton<IBlobPromptStore>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DashboardOptions>>().Value;
    var container = CreatePromptsContainer(opts);
    return new BlobPromptStore(container);
});

builder.Services.AddSingleton<PromptArtefactLoader>();
builder.Services.AddHostedService<AuditFeedService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();

static TableClient CreateAuditTableClient(DashboardOptions opts)
{
    if (opts.UseDevelopmentStorage)
    {
        var serviceClient = new TableServiceClient("UseDevelopmentStorage=true");
        var client = serviceClient.GetTableClient("AuditEvents");
        client.CreateIfNotExists();
        return client;
    }

    if (string.IsNullOrWhiteSpace(opts.AuditStorageAccountName))
    {
        throw new InvalidOperationException(
            "Dashboard:AuditStorageAccountName must be set when UseDevelopmentStorage is false.");
    }

    var uri = new Uri($"https://{opts.AuditStorageAccountName}.table.core.windows.net");
    return new TableClient(uri, "AuditEvents", new DefaultAzureCredential());
}

static BlobContainerClient CreatePromptsContainer(DashboardOptions opts)
{
    if (opts.UseDevelopmentStorage)
    {
        var serviceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        var container = serviceClient.GetBlobContainerClient("prompts");
        container.CreateIfNotExists();
        return container;
    }

    var uri = new Uri($"https://{opts.AuditStorageAccountName}.blob.core.windows.net/prompts");
    return new BlobContainerClient(uri, new DefaultAzureCredential());
}
