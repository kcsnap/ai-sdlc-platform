namespace AiSdlc.ModelProviders;

public sealed record ModelProviderOptions
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public int DefaultMaxTokens { get; init; } = 2048;

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
