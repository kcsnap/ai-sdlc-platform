using System.Net.Http.Headers;

namespace AiSdlc.ModelProviders;

public sealed record AnthropicRateLimiterOptions
{
    public int MaxConcurrentRequests { get; init; } = 2;

    // Safety ceiling on any single wait so bad headers or clock skew cannot park a
    // request forever — budgets replenish continuously, so a minute is normally enough.
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(90);
}

/// <summary>
/// Client-side throttle that keeps the platform inside its Anthropic usage-tier limits
/// instead of discovering them as 429s. Adaptive: every response carries
/// anthropic-ratelimit-{requests,input-tokens,output-tokens}-{limit,remaining,reset}
/// headers, which this records; before each request it waits until the advertised
/// budgets cover the request. No tier numbers are hardcoded — if the org's limits
/// change, the throttle follows. Register as a singleton: the whole point is that
/// parallel agent fan-outs share one budget view.
/// Throttling is per worker instance; the provider's 429 retry remains the backstop
/// for anything that slips through (e.g. scale-out to multiple instances).
/// </summary>
public sealed class AnthropicRateLimiter
{
    private static readonly string[] HeaderPrefixes =
    [
        "anthropic-ratelimit-requests",
        "anthropic-ratelimit-input-tokens",
        "anthropic-ratelimit-output-tokens"
    ];

    private readonly SemaphoreSlim _concurrency;
    private readonly AnthropicRateLimiterOptions _options;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private Budget _requests     = Budget.Unknown;
    private Budget _inputTokens  = Budget.Unknown;
    private Budget _outputTokens = Budget.Unknown;

    public AnthropicRateLimiter(AnthropicRateLimiterOptions options, TimeProvider? timeProvider = null)
    {
        _options     = options;
        _time        = timeProvider ?? TimeProvider.System;
        _concurrency = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);
    }

    /// <summary>
    /// Blocks until a concurrency slot is free and the known rate-limit budgets cover the
    /// request, then reserves the estimated usage. Dispose the lease when the call finishes.
    /// Output is reserved at the full max-token value — conservative, but self-correcting:
    /// the next response's headers replace the reservation with the real remaining budget.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(
        long estimatedInputTokens, long maxOutputTokens, CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                TimeSpan delay;
                lock (_gate)
                {
                    var now = _time.GetUtcNow();
                    ReplenishExpired(now);
                    delay = ComputeDelay(estimatedInputTokens, maxOutputTokens, now);
                    if (delay <= TimeSpan.Zero)
                    {
                        Reserve(estimatedInputTokens, maxOutputTokens);
                        return new Lease(_concurrency);
                    }
                }

                if (delay > _options.MaxDelay) delay = _options.MaxDelay;
                await Task.Delay(delay, _time, cancellationToken);
            }
        }
        catch
        {
            _concurrency.Release();
            throw;
        }
    }

    /// <summary>Records the rate-limit budget headers from an Anthropic response.</summary>
    public void RecordResponse(HttpResponseMessage response)
    {
        var requests = ParseBudget(response.Headers, HeaderPrefixes[0]);
        var input    = ParseBudget(response.Headers, HeaderPrefixes[1]);
        var output   = ParseBudget(response.Headers, HeaderPrefixes[2]);

        lock (_gate)
        {
            if (requests.IsKnown) _requests     = requests;
            if (input.IsKnown)    _inputTokens  = input;
            if (output.IsKnown)   _outputTokens = output;
        }
    }

    // ── Internals (testable) ──────────────────────────────────────────────────

    internal readonly record struct Budget(long Limit, long Remaining, DateTimeOffset ResetAt)
    {
        public static readonly Budget Unknown = new(-1, -1, DateTimeOffset.MinValue);
        public bool IsKnown => Limit >= 0;
    }

    internal static Budget ParseBudget(HttpResponseHeaders headers, string prefix)
    {
        if (!TryGetLong(headers, $"{prefix}-limit", out var limit) ||
            !TryGetLong(headers, $"{prefix}-remaining", out var remaining) ||
            !headers.TryGetValues($"{prefix}-reset", out var resetValues) ||
            !DateTimeOffset.TryParse(resetValues.FirstOrDefault(), out var resetAt))
        {
            return Budget.Unknown;
        }

        return new Budget(limit, remaining, resetAt);

        static bool TryGetLong(HttpResponseHeaders h, string name, out long value)
        {
            value = 0;
            return h.TryGetValues(name, out var values) && long.TryParse(values.FirstOrDefault(), out value);
        }
    }

    // The API replenishes continuously (token bucket); the reset header is when the
    // budget is FULL again, so treating it as the replenish point is conservative.
    private void ReplenishExpired(DateTimeOffset now)
    {
        if (_requests.IsKnown && now >= _requests.ResetAt)
            _requests = _requests with { Remaining = _requests.Limit };
        if (_inputTokens.IsKnown && now >= _inputTokens.ResetAt)
            _inputTokens = _inputTokens with { Remaining = _inputTokens.Limit };
        if (_outputTokens.IsKnown && now >= _outputTokens.ResetAt)
            _outputTokens = _outputTokens with { Remaining = _outputTokens.Limit };
    }

    internal TimeSpan ComputeDelay(long estimatedInputTokens, long maxOutputTokens, DateTimeOffset now)
    {
        var delay = DelayFor(_requests, 1, now);
        var inputDelay = DelayFor(_inputTokens, estimatedInputTokens, now);
        if (inputDelay > delay) delay = inputDelay;
        var outputDelay = DelayFor(_outputTokens, maxOutputTokens, now);
        if (outputDelay > delay) delay = outputDelay;
        return delay;

        static TimeSpan DelayFor(Budget budget, long needed, DateTimeOffset now)
        {
            if (!budget.IsKnown) return TimeSpan.Zero;

            // A request bigger than the whole limit can never fully fit — admit it once
            // the bucket is full and let the server meter the overage.
            if (needed > budget.Limit) needed = budget.Limit;

            return budget.Remaining >= needed ? TimeSpan.Zero : budget.ResetAt - now;
        }
    }

    private void Reserve(long estimatedInputTokens, long maxOutputTokens)
    {
        if (_requests.IsKnown)
            _requests = _requests with { Remaining = _requests.Remaining - 1 };
        if (_inputTokens.IsKnown)
            _inputTokens = _inputTokens with { Remaining = _inputTokens.Remaining - estimatedInputTokens };
        if (_outputTokens.IsKnown)
            _outputTokens = _outputTokens with { Remaining = _outputTokens.Remaining - maxOutputTokens };
    }

    private sealed class Lease(SemaphoreSlim concurrency) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                concurrency.Release();
        }
    }
}
