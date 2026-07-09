namespace AiSdlc.ModelProviders;

public sealed record ModelProviderOptions
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public int DefaultMaxTokens { get; init; } = 2048;

    /// <summary>
    /// Marks the system prompt and the context-document prefix with <c>cache_control</c> so Anthropic
    /// reuses them across the many calls that share them — the Code Implementer's batch + recovery loop,
    /// the CI-repair re-runs, and the parallel reviewers all re-send an identical large prefix that
    /// differs only in the trailing instruction. Cached input reads at ~10% of the base rate and lowers
    /// TTFT. On by default; set <c>AnthropicPromptCaching=false</c> to disable (e.g. to isolate a cost
    /// regression). Blocks below the model's minimum cacheable size are silently not cached by the API,
    /// so this never errors on small prompts.
    /// </summary>
    public bool EnablePromptCaching { get; init; } = true;

    /// <summary>
    /// Per-agent model overrides (agent name → model id). A request whose <c>AgentName</c> is keyed
    /// here runs on the mapped model; every other agent uses the global <see cref="ModelName"/>.
    /// Lets the design-critical steps run on a stronger (costlier) model while cheap agents stay on
    /// the default. Empty by default — no behaviour change unless configured.
    /// </summary>
    public IReadOnlyDictionary<string, string> ModelOverridesByAgent { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Parses an override spec of the form <c>Agent One=model-a;Agent Two=model-b</c> into a map.
    /// Entries are <c>;</c>-separated and split on the first <c>=</c> (agent names contain spaces and
    /// '/', never ';' or '='). Lookups are case-insensitive. Null/blank/malformed input → empty map.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseOverrides(string? spec)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(spec)) return map;

        foreach (var entry in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = entry.IndexOf('=');
            if (eq <= 0) continue;

            var agent = entry[..eq].Trim();
            var model = entry[(eq + 1)..].Trim();
            if (agent.Length > 0 && model.Length > 0)
                map[agent] = model;
        }

        return map;
    }
}
