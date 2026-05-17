using System.Collections.Concurrent;

namespace AiSdlc.Dashboard.Services;

// In-memory pub/sub fan-out. Components subscribe with a handler; the poller publishes batches.
// Also keeps a bounded ring buffer of the most recent events so newly connecting clients see history.
public sealed class DashboardEventBus
{
    private readonly object _bufferLock = new();
    private readonly LinkedList<DashboardEvent> _recent = new();
    private readonly ConcurrentDictionary<Guid, Func<IReadOnlyList<DashboardEvent>, Task>> _subscribers = new();
    private readonly int _capacity;

    public DashboardEventBus(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public IReadOnlyList<DashboardEvent> Snapshot()
    {
        lock (_bufferLock)
        {
            return _recent.ToArray();
        }
    }

    public IDisposable Subscribe(Func<IReadOnlyList<DashboardEvent>, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = Guid.NewGuid();
        _subscribers[id] = handler;
        return new Subscription(this, id);
    }

    public async Task PublishAsync(IReadOnlyList<DashboardEvent> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        lock (_bufferLock)
        {
            foreach (var ev in batch)
            {
                _recent.AddLast(ev);
            }

            while (_recent.Count > _capacity)
            {
                _recent.RemoveFirst();
            }
        }

        foreach (var subscriber in _subscribers.Values)
        {
            try
            {
                await subscriber(batch);
            }
            catch
            {
                // A failing subscriber must not stop other subscribers from receiving the batch.
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly DashboardEventBus _bus;
        private readonly Guid _id;
        public Subscription(DashboardEventBus bus, Guid id) { _bus = bus; _id = id; }
        public void Dispose() => _bus._subscribers.TryRemove(_id, out _);
    }
}
