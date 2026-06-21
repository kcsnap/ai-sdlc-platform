using System.Text;
using System.Text.Json;

namespace AiSdlc.Orchestrator.Imagery;

/// <summary>One real, hotlinkable stock photo with the metadata the prompt manifest needs.</summary>
public sealed record StockImage(string Url, string Description, int Width, int Height, string Credit);

/// <summary>
/// Source of real photography for generated marketing pages. The imagery step decides per-brand whether
/// a photo would lift the design (default no); when it would, this turns the chosen search queries into
/// real, hotlinkable URLs so the implementer never invents a 404ing src. The API key lives in platform
/// config and is used server-side only — the page receives public image URLs, never the key.
/// </summary>
public interface IImageSource
{
    /// <summary>Run the queries and render a manifest the generation prompt can consume, or null if none.</summary>
    Task<string?> BuildManifestAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken);
}

/// <summary>Used when no PexelsApiKey is configured — imagery stays generative-only (safe default).</summary>
public sealed class NoOpImageSource : IImageSource
{
    public Task<string?> BuildManifestAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}

/// <summary>Pexels-backed <see cref="IImageSource"/>. The HttpClient is pre-configured (base address +
/// Authorization header) in Program.cs only when a key is present.</summary>
public sealed class PexelsImageSource : IImageSource
{
    private readonly HttpClient _http;

    public PexelsImageSource(HttpClient http) => _http = http;

    public async Task<string?> BuildManifestAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queries);
        if (queries.Count == 0) return null;

        var images = new List<StockImage>();
        foreach (var query in queries)
            images.AddRange(await SearchAsync(query, count: 2, cancellationToken));

        if (images.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var img in images)
            sb.AppendLine($"- {img.Url} — \"{img.Description}\" ({img.Width}x{img.Height}) — © {img.Credit}");
        return sb.ToString().TrimEnd();
    }

    private async Task<IReadOnlyList<StockImage>> SearchAsync(string query, int count, CancellationToken cancellationToken)
    {
        var url = $"search?query={Uri.EscapeDataString(query)}&per_page={count}&orientation=landscape";
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("photos", out var photos) || photos.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<StockImage>();
        foreach (var p in photos.EnumerateArray())
        {
            var src = p.TryGetProperty("src", out var s) && s.TryGetProperty("large", out var large)
                ? large.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(src)) continue;

            var alt          = p.TryGetProperty("alt", out var a) ? a.GetString() : null;
            var photographer = p.TryGetProperty("photographer", out var ph) ? ph.GetString() : "Unknown";
            var width        = p.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : 0;
            var height       = p.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : 0;

            results.Add(new StockImage(
                Url: src!,
                Description: string.IsNullOrWhiteSpace(alt) ? query : alt!,
                Width: width,
                Height: height,
                Credit: $"{photographer} / Pexels"));
        }

        return results;
    }
}
