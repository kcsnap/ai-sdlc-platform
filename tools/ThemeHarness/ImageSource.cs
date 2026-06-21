using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ThemeHarness;

/// <summary>One real, hotlinkable stock photo with the metadata the prompt manifest needs.</summary>
public sealed record StockImage(string Url, string Description, int Width, int Height, string Credit);

/// <summary>
/// Pexels-backed source of real photography. Tier-1 imagery is generative by default; when the
/// imagery-plan step decides a human/lifestyle photo would genuinely lift a brand, this fetches
/// real URLs so the model never invents a 404ing src. Returns null from <see cref="FromEnvironment"/>
/// when no key is set, so the harness falls back to pure generative output unchanged.
/// </summary>
public sealed class ImageSource
{
    private readonly HttpClient _http;

    private ImageSource(HttpClient http) => _http = http;

    public static ImageSource? FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable("PexelsApiKey");
        if (string.IsNullOrWhiteSpace(key)) return null;

        var http = new HttpClient { BaseAddress = new Uri("https://api.pexels.com/v1/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(key);
        http.Timeout = TimeSpan.FromSeconds(30);
        return new ImageSource(http);
    }

    /// <summary>Search Pexels for one query; returns up to <paramref name="count"/> landscape photos.</summary>
    public async Task<IReadOnlyList<StockImage>> SearchAsync(string query, int count, CancellationToken cancellationToken)
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

            var alt = p.TryGetProperty("alt", out var a) ? a.GetString() : null;
            var photographer = p.TryGetProperty("photographer", out var ph) ? ph.GetString() : "Unknown";
            var width = p.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : 0;
            var height = p.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : 0;

            results.Add(new StockImage(
                Url: src!,
                Description: string.IsNullOrWhiteSpace(alt) ? query : alt!,
                Width: width,
                Height: height,
                Credit: $"{photographer} / Pexels"));
        }

        return results;
    }

    /// <summary>
    /// Run the queries the imagery-plan chose and render a manifest the main prompt can consume,
    /// or null when nothing usable came back (the prompt then stays fully generative).
    /// </summary>
    public async Task<string?> BuildManifestAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken)
    {
        var images = new List<StockImage>();
        foreach (var query in queries)
            images.AddRange(await SearchAsync(query, count: 2, cancellationToken));

        if (images.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var img in images)
            sb.AppendLine($"- {img.Url} — \"{img.Description}\" ({img.Width}x{img.Height}) — © {img.Credit}");
        return sb.ToString().TrimEnd();
    }
}
