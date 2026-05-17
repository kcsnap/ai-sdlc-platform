using AiSdlc.Audit;
using AiSdlc.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiSdlc.Dashboard.Services;

// Tails the audit-events store and pushes new records into the bus.
public sealed class AuditFeedService : BackgroundService
{
    private readonly IAuditService _audit;
    private readonly DashboardEventBus _bus;
    private readonly DashboardOptions _options;
    private readonly ILogger<AuditFeedService> _logger;

    public AuditFeedService(
        IAuditService audit,
        DashboardEventBus bus,
        IOptions<DashboardOptions> options,
        ILogger<AuditFeedService> logger)
    {
        _audit   = audit;
        _bus     = bus;
        _options = options.Value;
        _logger  = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start the tail from "now minus BackfillHours" so a freshly-opened dashboard shows recent
        // activity without scanning the entire table. After the first poll the high-water mark
        // advances to the latest seen event.
        var backfillHours = Math.Max(1, _options.BackfillHours);
        var highWaterMark = DateTimeOffset.UtcNow - TimeSpan.FromHours(backfillHours);

        _logger.LogInformation(
            "AuditFeedService started. Polling every {Interval}s, backfilling {Hours}h from {Since:O}.",
            _options.PollIntervalSeconds, backfillHours, highWaterMark);

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _audit.GetSinceAsync(highWaterMark, _options.BackfillSize, stoppingToken);

                _logger.LogInformation("Audit poll: fetched {Count} events since {Since:O}.", batch.Count, highWaterMark);

                if (batch.Count > 0)
                {
                    highWaterMark = MaxTimestamp(batch, highWaterMark);

                    var projected = batch
                        .Select(DashboardEvent.FromAuditEvent)
                        .ToArray();

                    await _bus.PublishAsync(projected);

                    _logger.LogInformation("Published {Count} audit events. New highWater={HighWater:O}.", projected.Length, highWaterMark);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuditFeedService poll failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("AuditFeedService stopped.");
    }

    private static DateTimeOffset MaxTimestamp(IReadOnlyList<AuditEvent> events, DateTimeOffset current)
    {
        var max = current;
        foreach (var e in events)
        {
            if (e.TimestampUtc > max)
            {
                max = e.TimestampUtc;
            }
        }

        return max;
    }
}
