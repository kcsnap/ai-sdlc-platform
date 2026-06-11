using System.Net;
using AiSdlc.ModelProviders;
using Xunit;

namespace AiSdlc.ModelProviders.Tests;

public sealed class AnthropicRateLimiterTests
{
    private static readonly DateTimeOffset Now = new(2026, 06, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseBudget_ReadsAllThreeHeaders()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("anthropic-ratelimit-output-tokens-limit", "10000");
        response.Headers.Add("anthropic-ratelimit-output-tokens-remaining", "2000");
        response.Headers.Add("anthropic-ratelimit-output-tokens-reset", "2026-06-11T12:00:30Z");

        var budget = AnthropicRateLimiter.ParseBudget(response.Headers, "anthropic-ratelimit-output-tokens");

        Assert.True(budget.IsKnown);
        Assert.Equal(10000, budget.Limit);
        Assert.Equal(2000, budget.Remaining);
        Assert.Equal(new DateTimeOffset(2026, 06, 11, 12, 0, 30, TimeSpan.Zero), budget.ResetAt);
    }

    [Fact]
    public void ParseBudget_MissingHeaders_ReturnsUnknown()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var budget = AnthropicRateLimiter.ParseBudget(response.Headers, "anthropic-ratelimit-requests");
        Assert.False(budget.IsKnown);
    }

    [Fact]
    public async Task Acquire_WithNoHeaderKnowledge_AdmitsImmediately()
    {
        var limiter = new AnthropicRateLimiter(new AnthropicRateLimiterOptions());
        using var lease = await limiter.AcquireAsync(1000, 8000, CancellationToken.None);
        Assert.NotNull(lease);
    }

    [Fact]
    public void ComputeDelay_BudgetCoversRequest_NoDelay()
    {
        var limiter = MakeLimiterWithOutputBudget(limit: 10000, remaining: 9000, resetAt: Now.AddSeconds(30));
        Assert.Equal(TimeSpan.Zero, limiter.ComputeDelay(estimatedInputTokens: 100, maxOutputTokens: 8000, Now));
    }

    [Fact]
    public void ComputeDelay_OutputBudgetExhausted_WaitsUntilReset()
    {
        var limiter = MakeLimiterWithOutputBudget(limit: 10000, remaining: 2000, resetAt: Now.AddSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), limiter.ComputeDelay(estimatedInputTokens: 100, maxOutputTokens: 8000, Now));
    }

    [Fact]
    public void ComputeDelay_RequestBiggerThanWholeLimit_AdmitsOnFullBucket()
    {
        // A 20k-output request can never fit a 10k budget — admit once the bucket is full
        // rather than waiting forever.
        var limiter = MakeLimiterWithOutputBudget(limit: 10000, remaining: 10000, resetAt: Now.AddSeconds(30));
        Assert.Equal(TimeSpan.Zero, limiter.ComputeDelay(estimatedInputTokens: 100, maxOutputTokens: 20000, Now));
    }

    [Fact]
    public async Task Acquire_ExhaustedBudgetWithPastReset_AdmitsViaReplenish()
    {
        // The reset time recorded from the last response is already in the past — the
        // bucket has refilled, so the request must not wait.
        var limiter = new AnthropicRateLimiter(new AnthropicRateLimiterOptions());
        limiter.RecordResponse(OutputBudgetResponse(limit: 10000, remaining: 0, resetAt: DateTimeOffset.UtcNow.AddSeconds(-5)));

        using var lease = await limiter.AcquireAsync(100, 8000, CancellationToken.None);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task Acquire_ReservesBudget_SoParallelCallersSeeReducedHeadroom()
    {
        var limiter = new AnthropicRateLimiter(new AnthropicRateLimiterOptions());
        limiter.RecordResponse(OutputBudgetResponse(limit: 10000, remaining: 10000, resetAt: DateTimeOffset.UtcNow.AddMinutes(5)));

        using var first = await limiter.AcquireAsync(100, 8000, CancellationToken.None);

        // 10000 - 8000 reserved = 2000 left; a second 8000-token request must now wait.
        var delay = limiter.ComputeDelay(estimatedInputTokens: 100, maxOutputTokens: 8000, DateTimeOffset.UtcNow);
        Assert.True(delay > TimeSpan.Zero);
    }

    [Fact]
    public async Task Acquire_ConcurrencyCap_BlocksUntilLeaseDisposed()
    {
        var limiter = new AnthropicRateLimiter(new AnthropicRateLimiterOptions { MaxConcurrentRequests = 1 });

        var first  = await limiter.AcquireAsync(10, 10, CancellationToken.None);
        var second = limiter.AcquireAsync(10, 10, CancellationToken.None);

        Assert.False(second.IsCompleted);
        first.Dispose();
        using var lease = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static AnthropicRateLimiter MakeLimiterWithOutputBudget(long limit, long remaining, DateTimeOffset resetAt)
    {
        var limiter = new AnthropicRateLimiter(new AnthropicRateLimiterOptions());
        limiter.RecordResponse(OutputBudgetResponse(limit, remaining, resetAt));
        return limiter;
    }

    private static HttpResponseMessage OutputBudgetResponse(long limit, long remaining, DateTimeOffset resetAt)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("anthropic-ratelimit-output-tokens-limit", limit.ToString());
        response.Headers.Add("anthropic-ratelimit-output-tokens-remaining", remaining.ToString());
        response.Headers.Add("anthropic-ratelimit-output-tokens-reset", resetAt.ToString("o"));
        return response;
    }
}
