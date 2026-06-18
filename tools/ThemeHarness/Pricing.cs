namespace ThemeHarness;

/// <summary>
/// Per-model token pricing (USD per million tokens), for benchmarking spend.
/// Source: claude-api skill model table (cached 2026-05-26). No prompt caching is used
/// by the harness (single-shot generations), so cost is simply input*rate + output*rate.
/// </summary>
public static class Pricing
{
    private sealed record Rate(decimal InputPerMTok, decimal OutputPerMTok);

    private static readonly IReadOnlyDictionary<string, Rate> Rates = new Dictionary<string, Rate>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-fable-5"]    = new(10.00m, 50.00m),
        ["claude-opus-4-8"]   = new(5.00m, 25.00m),
        ["claude-sonnet-4-6"] = new(3.00m, 15.00m),
        ["claude-haiku-4-5"]  = new(1.00m, 5.00m),
    };

    /// <summary>Computes USD cost for a single generation, or null if the model's price is unknown.</summary>
    public static decimal? Cost(string model, long inputTokens, long outputTokens)
    {
        // Match on the leading known id (handles dated suffixes like claude-haiku-4-5-20251001).
        var rate = Rates.FirstOrDefault(r => model.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase)).Value;
        if (rate is null) return null;
        return inputTokens / 1_000_000m * rate.InputPerMTok
             + outputTokens / 1_000_000m * rate.OutputPerMTok;
    }
}
