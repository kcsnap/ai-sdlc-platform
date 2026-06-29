using Yorrixx.Contracts.Hosting;
using Yorrixx.Modules.Hosting;
using Yorrixx.Provisioner.Contracts;
using Yorrixx.Provisioner.Internal;

var builder = WebApplication.CreateBuilder(args);

// Wire the Hosting module — registers ArmClient + IHostingService +
// IUserAppDeployIdentityProvisioner + IClerkOrgProvisioner from the Hosting:*
// config (real impls when SubscriptionId/TenantId/ClerkSecretKey are set, stubs
// otherwise so the service boots locally without the high-privilege grants).
new HostingModule().Register(builder.Services, builder.Configuration);

builder.Services.Configure<PlatformCallbackOptions>(
    builder.Configuration.GetSection(PlatformCallbackOptions.SectionName));
builder.Services.Configure<ProvisionWorkerOptions>(
    builder.Configuration.GetSection(ProvisionWorkerOptions.SectionName));

builder.Services.AddSingleton<ProvisionStore>();
builder.Services.AddSingleton<ProvisionQueue>();
builder.Services.AddHttpClient<PlatformCallbackClient>();
builder.Services.AddHostedService<ProvisionWorker>();

var app = builder.Build();

// Inbound auth: the platform presents X-Platform-Provision-Key on every call
// (extends the Phase-0 X-Yorrixx-Admin-Key pattern). Constant-time-ish equality
// is overkill for a shared internal key; a plain compare is fine here.
var inboundKey = builder.Configuration["Provisioner:InboundKey"];
bool Authorized(HttpContext ctx) =>
    !string.IsNullOrEmpty(inboundKey) &&
    ctx.Request.Headers.TryGetValue("X-Platform-Provision-Key", out var k) &&
    string.Equals(k.ToString(), inboundKey, StringComparison.Ordinal);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Call 1 — provision (async): validate, accept, enqueue, 202.
app.MapPost("/provision", (ProvisionSpec spec, ProvisionStore store, ProvisionQueue queue, HttpContext ctx) =>
{
    if (!Authorized(ctx)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(spec.AppId) || string.IsNullOrWhiteSpace(spec.BuildId) ||
        spec.Repo is null || string.IsNullOrWhiteSpace(spec.Repo.Name))
    {
        return Results.BadRequest(new { error = "appId, buildId and repo are required" });
    }

    store.SetAccepted(spec.BuildId);
    queue.Enqueue(spec);
    return Results.Accepted($"/provision/{spec.BuildId}", new ProvisionAccepted(spec.BuildId, "accepted"));
});

// GET /provision/{buildId} — poll fallback for a dropped Call-2 callback.
app.MapGet("/provision/{buildId}", (string buildId, ProvisionStore store, HttpContext ctx) =>
{
    if (!Authorized(ctx)) return Results.Unauthorized();
    var status = store.Get(buildId);
    return status is null ? Results.NotFound() : Results.Ok(status);
});

// Call 3 — deprovision. The platform sends only {appId}; HostingService.DeprovisionByAppIdAsync
// tears down by the appId-derived id8 (every resource name embeds it) + the appId-keyed Clerk Org
// and deploy identity. Best-effort + idempotent (a missing resource is success); catastrophic
// failures (e.g. RG unreachable) surface as outcome "failed".
app.MapPost("/deprovision", async (DeprovisionRequest req, IHostingService hosting, HttpContext ctx) =>
{
    if (!Authorized(ctx)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.AppId))
        return Results.BadRequest(new { error = "appId is required" });
    try
    {
        await hosting.DeprovisionByAppIdAsync(req.AppId, ctx.RequestAborted);
        return Results.Ok(new DeprovisionResult("deprovisioned", null));
    }
    catch (Exception ex)
    {
        return Results.Ok(new DeprovisionResult("failed", ex.Message));
    }
});

// Call 4 — hosting spend by appId tag. TODO (slice 3b): wire the Azure
// Cost-Management query (depends on the mandatory appId tag being stamped on
// every resource). Returns zero until then so the platform's daily relay is
// inert rather than erroring.
app.MapGet("/spend", (string appId, HttpContext ctx) =>
{
    if (!Authorized(ctx)) return Results.Unauthorized();
    return Results.Ok(new HostingSpend("GBP", MonthToDateMinor: 0, AsOf: DateTimeOffset.UtcNow));
});

app.Run();
