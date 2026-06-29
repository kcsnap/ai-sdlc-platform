using System.Collections.Concurrent;
using System.Threading.Channels;
using Yorrixx.Provisioner.Contracts;

namespace Yorrixx.Provisioner.Internal;

/// State held per buildId so the GET /provision/{buildId} poll fallback can
/// report progress even if the provision-result callback was dropped (the
/// stuck-instance lesson). Single-process, in-memory — same trade-off as the
/// platform's Durable instance + the Yorrixx app's in-memory queues.
public sealed class ProvisionStore
{
    private readonly ConcurrentDictionary<string, ProvisionStatus> _byBuildId = new();

    public void SetAccepted(string buildId) =>
        _byBuildId[buildId] = new ProvisionStatus("accepted", Result: null);

    public void SetResult(string buildId, ProvisionResult result) =>
        _byBuildId[buildId] = new ProvisionStatus(
            result.Outcome == "provisioned" ? "provisioned" : "failed", result);

    public ProvisionStatus? Get(string buildId) =>
        _byBuildId.TryGetValue(buildId, out var s) ? s : null;
}

public sealed record ProvisionStatus(string Status, ProvisionResult? Result);

/// In-memory provision work queue. POST /provision enqueues + returns 202; the
/// ProvisionWorker drains it so provisioning (minutes) is decoupled from the
/// request. Idempotent on buildId is enforced by the deterministic appId-based
/// resource naming in HostingService (re-running converges), so a duplicate
/// enqueue is harmless.
public sealed class ProvisionQueue
{
    private readonly Channel<ProvisionSpec> _channel =
        Channel.CreateUnbounded<ProvisionSpec>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(ProvisionSpec spec) => _channel.Writer.TryWrite(spec);

    public IAsyncEnumerable<ProvisionSpec> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
