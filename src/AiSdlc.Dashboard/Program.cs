using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using AiSdlc.Audit;
using AiSdlc.Dashboard.Components;
using AiSdlc.Dashboard.Services;
using AiSdlc.Dashboard.Services.YorrixxAdmin;
using Microsoft.Extensions.Options;

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

builder.Services.Configure<YorrixxAdminOptions>(
    builder.Configuration.GetSection(YorrixxAdminOptions.SectionName));

builder.Services.AddHttpClient<IYorrixxAdminClient, YorrixxAdminClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<YorrixxAdminOptions>>().Value;
    if (string.IsNullOrWhiteSpace(opts.BaseUrl))
    {
        throw new InvalidOperationException(
            "YorrixxAdmin:BaseUrl must be set (see appsettings.Development.json).");
    }
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    if (!string.IsNullOrWhiteSpace(opts.ApiKey))
    {
        client.DefaultRequestHeaders.Add("X-Yorrixx-Admin-Key", opts.ApiKey);
    }
});

// GitHub API fallback for issue title/state when audit data lacks it (e.g. runs created before
// the orchestrator started writing webhook audit events).
builder.Services.AddHttpClient<IGitHubIssueLookup, GitHubIssueLookup>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ai-sdlc-dashboard/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

    var pat = Environment.GetEnvironmentVariable("GitHubPat");
    if (!string.IsNullOrWhiteSpace(pat))
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pat);
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Tell browsers to always revalidate static assets so CSS / SVG edits show up immediately
// without needing a hard refresh. ETag still gives us cheap 304s when nothing has changed.
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, must-revalidate"
});
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
