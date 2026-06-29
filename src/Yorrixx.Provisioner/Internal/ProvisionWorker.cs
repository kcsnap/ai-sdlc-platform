using Microsoft.Extensions.Options;
using Yorrixx.Contracts.Hosting;
using Yorrixx.Provisioner.Contracts;

namespace Yorrixx.Provisioner.Internal;

public sealed class ProvisionWorkerOptions
{
    public const string SectionName = "Hosting"; // reuse the Hosting section's Subscription/RG
    public string SubscriptionId { get; init; } = "";
    public string ResourceGroup { get; init; } = "";
}

/// Drains the provision queue: maps the spec → capabilities, runs the
/// (capability-aware) HostingService, re-fetches the repo-federated deploy
/// identity, builds the provision-result, stores it for the poll fallback, and
/// posts the callback. Runs decoupled from the HTTP request that accepted the
/// 202. Uses CancellationToken.None for the terminal store/callback so a result
/// always lands even on shutdown.
public sealed class ProvisionWorker(
    ProvisionQueue queue,
    ProvisionStore store,
    IServiceScopeFactory scopes,
    PlatformCallbackClient callback,
    IOptions<ProvisionWorkerOptions> options,
    ILogger<ProvisionWorker> logger) : BackgroundService
{
    private readonly ProvisionWorkerOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("provision worker started");
        try
        {
            await foreach (var spec in queue.ReadAllAsync(stoppingToken))
            {
                await RunOne(spec, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // host shutdown — clean exit
        }
        finally
        {
            logger.LogInformation("provision worker stopped");
        }
    }

    private async Task RunOne(ProvisionSpec spec, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var hosting = scope.ServiceProvider.GetRequiredService<IHostingService>();
        var deployIdentity = scope.ServiceProvider.GetRequiredService<IUserAppDeployIdentityProvisioner>();

        try
        {
            var caps = ProvisionMapper.ToCapabilities(spec.Capabilities);

            // The provisioner owns HOW: appName feeds the resource slug. We pass
            // the repo name as the app name so naming stays stable across the
            // build (the platform doesn't send a separate display name).
            var deployed = await hosting.EnsureProvisionedAsync(
                spec.AppId, ownerUserId: spec.AppId, appName: spec.Repo.Name, caps, ct);

            var identity = await deployIdentity.EnsureAsync(
                spec.AppId, spec.Repo.Owner, spec.Repo.Name, spec.Repo.DefaultBranch, ct);

            var result = ProvisionMapper.ToResult(spec, deployed, identity, _opts.SubscriptionId, _opts.ResourceGroup);
            store.SetResult(spec.BuildId, result);
            await callback.PostResultAsync(result, CancellationToken.None);
            logger.LogInformation("provisioned appId={AppId} buildId={BuildId}", spec.AppId, spec.BuildId);
        }
        catch (Exception ex)
        {
            // Provisioner owns rollback: the idempotent helpers + name-derived
            // teardown mean a retry converges; we report failed so the platform
            // never proceeds to deploy on a half-built stack.
            logger.LogError(ex, "provision failed appId={AppId} buildId={BuildId}", spec.AppId, spec.BuildId);
            var failed = ProvisionMapper.Failed(spec, ex.Message);
            store.SetResult(spec.BuildId, failed);
            await callback.PostResultAsync(failed, CancellationToken.None);
        }
    }
}
