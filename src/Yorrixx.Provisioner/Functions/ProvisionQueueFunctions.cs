using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yorrixx.Contracts.Hosting;
using Yorrixx.Provisioner.Contracts;
using Yorrixx.Provisioner.Internal;

namespace Yorrixx.Provisioner.Functions;

/// The decoupled work half — queue-triggered so a full provision (minutes) runs inside the Flex
/// Consumption per-invocation budget, not an HTTP request. Replaces the old in-proc BackgroundService.
public sealed class ProvisionQueueFunctions
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHostingService _hosting;
    private readonly IUserAppDeployIdentityProvisioner _deployIdentity;
    private readonly PlatformCallbackClient _callback;
    private readonly TableProvisionStore _store;
    private readonly ProvisionWorkerOptions _opts;
    private readonly ILogger<ProvisionQueueFunctions> _logger;

    public ProvisionQueueFunctions(
        IHostingService hosting,
        IUserAppDeployIdentityProvisioner deployIdentity,
        PlatformCallbackClient callback,
        TableProvisionStore store,
        IOptions<ProvisionWorkerOptions> opts,
        ILogger<ProvisionQueueFunctions> logger)
    {
        _hosting        = hosting;
        _deployIdentity = deployIdentity;
        _callback       = callback;
        _store          = store;
        _opts           = opts.Value;
        _logger         = logger;
    }

    [Function("provision_worker")]
    public async Task ProvisionWorkerAsync(
        [QueueTrigger("provision-queue", Connection = "AzureWebJobsStorage")] string message,
        CancellationToken cancellationToken)
    {
        var spec = JsonSerializer.Deserialize<ProvisionSpec>(message, Json)
            ?? throw new InvalidOperationException("provision-queue message did not deserialize to a ProvisionSpec.");

        try
        {
            var caps = ProvisionMapper.ToCapabilities(spec.Capabilities);

            // The provisioner owns HOW: appName feeds the resource slug (fallback: repo name, the pre-G5
            // behaviour); ownerUserId is the owner's Clerk user id — empty means "no creator" (a placeholder
            // here 400s at Clerk with organization_creator_not_found, found the hard way in G5).
            var deployed = await _hosting.EnsureProvisionedAsync(
                spec.AppId,
                ownerUserId: spec.OwnerUserId ?? string.Empty,
                appName: string.IsNullOrWhiteSpace(spec.AppName) ? spec.Repo.Name : spec.AppName!,
                caps, cancellationToken);

            var identity = await _deployIdentity.EnsureAsync(
                spec.AppId, spec.Repo.Owner, spec.Repo.Name, spec.Repo.DefaultBranch,
                spec.Repo.OwnerId, spec.Repo.RepoId, cancellationToken);

            var result = ProvisionMapper.ToResult(spec, deployed, identity, _opts.SubscriptionId, _opts.ResourceGroup);
            _store.SetResult(spec.BuildId, result);
            await _callback.PostResultAsync(result, CancellationToken.None);
            _logger.LogInformation("provisioned appId={AppId} buildId={BuildId}", spec.AppId, spec.BuildId);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // D9: a HOST-initiated cancellation (Flex recycle/scale event) is not a provisioning
            // failure — wave 5 lost all five apps at the same millisecond to one recycle because
            // this path reported "A task was canceled." as a permanent build failure. Rethrow so
            // the queue redelivers to a healthy instance; the idempotent helpers converge on retry.
            // After max dequeues the message poisons and the build fails HONESTLY at the platform's
            // provision timeout instead of with a phantom cancellation.
            _logger.LogWarning(ex,
                "provision interrupted by host cancellation appId={AppId} buildId={BuildId} — requeueing for redelivery",
                spec.AppId, spec.BuildId);
            throw;
        }
        catch (Exception ex)
        {
            // Provisioner owns rollback: idempotent helpers + name-derived teardown mean a retry converges.
            // Report failed so the platform never proceeds to deploy on a half-built stack. We do NOT
            // rethrow — matching the prior worker's behaviour (report + move on, no host retry storm).
            _logger.LogError(ex, "provision failed appId={AppId} buildId={BuildId}", spec.AppId, spec.BuildId);
            var failed = ProvisionMapper.Failed(spec, ex.Message);
            _store.SetResult(spec.BuildId, failed);
            await _callback.PostResultAsync(failed, CancellationToken.None);
        }
    }

    [Function("deprovision_worker")]
    public async Task DeprovisionWorkerAsync(
        [QueueTrigger("deprovision-queue", Connection = "AzureWebJobsStorage")] string appId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("deprovision worker appId={AppId}", appId);
        await _hosting.DeprovisionByAppIdAsync(appId, cancellationToken);
    }
}
