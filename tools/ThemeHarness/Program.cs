using System.Diagnostics;
using System.Globalization;
using System.Text;
using AiSdlc.ModelProviders;
using AiSdlc.Shared.Redaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThemeHarness;

// Tier-1 theme harness — brief in, purely-visual themed static site out, previewed locally over HTTPS,
// with spend/time/token benchmarking across models.
//
//   dotnet run -- list
//   dotnet run -- generate  <slug|all>            [--model <id>] [--max-tokens <n>]
//   dotnet run -- benchmark <slug|all> --models a,b,c [--max-tokens <n>]
//   dotnet run -- serve     <slug>                 [--model <id>] [--port <n>]
//
// Requires the AnthropicApiKey environment variable (same key the platform uses).
// Single-model default model is AnthropicModel env var, else claude-sonnet-4-6.

var (command, positional, options) = ParseArgs(args);
var outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "output");

switch (command)
{
    case "list":
        ListBriefs();
        return 0;

    case "generate":
        return await Generate(positional, options, outputRoot);

    case "benchmark":
        return await Benchmark(positional, options, outputRoot);

    case "serve":
        return await Serve(positional, options, outputRoot);

    default:
        Console.WriteLine(
            """
            Tier-1 theme harness

            Usage:
              dotnet run -- list
              dotnet run -- generate  <slug|all>             [--model <id>] [--max-tokens <n>]
              dotnet run -- benchmark <slug|all> --models a,b,c [--max-tokens <n>]
              dotnet run -- serve     <slug>                  [--model <id>] [--port <n>]

            Set the AnthropicApiKey environment variable before generating.
            """);
        return command is null ? 0 : 1;
}

void ListBriefs()
{
    Console.WriteLine("Available customer briefs:\n");
    foreach (var b in Briefs.All)
        Console.WriteLine($"  {b.Slug,-20}  {b.BusinessName}  —  {b.Vertical}");
    Console.WriteLine("\nUse:  generate <slug>   |   generate all   |   benchmark <slug> --models claude-opus-4-8,claude-sonnet-4-6,claude-haiku-4-5");
}

IReadOnlyList<CustomerBrief> ResolveBriefs(string? target) =>
    string.Equals(target, "all", StringComparison.OrdinalIgnoreCase)
        ? Briefs.All
        : Briefs.Find(target ?? "") is { } one ? [one] : [];

async Task<int> Generate(string? target, IReadOnlyDictionary<string, string> opts, string root)
{
    if (string.IsNullOrWhiteSpace(target))
    {
        Console.Error.WriteLine("Specify a brief slug or 'all'. Try:  dotnet run -- list");
        return 1;
    }

    var briefs = ResolveBriefs(target);
    if (briefs.Count == 0)
    {
        Console.Error.WriteLine($"Unknown brief '{target}'. Try:  dotnet run -- list");
        return 1;
    }

    var model = opts.GetValueOrDefault("model")
        ?? Environment.GetEnvironmentVariable("AnthropicModel")
        ?? "claude-sonnet-4-6";

    IModelProvider provider;
    try { provider = BuildProvider(model); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

    var maxTokens = int.TryParse(opts.GetValueOrDefault("max-tokens"), out var mt) ? mt : 16000;

    foreach (var brief in briefs)
    {
        Console.WriteLine($"\n→ Generating {brief.Slug} ({brief.BusinessName}) …");
        var result = await GenerateOne(provider, model, brief, maxTokens, Path.Combine(root, brief.Slug));
        PrintResult(result);
    }

    Console.WriteLine("\nPreview with:  dotnet run -- serve <slug>");
    return 0;
}

async Task<int> Benchmark(string? target, IReadOnlyDictionary<string, string> opts, string root)
{
    if (string.IsNullOrWhiteSpace(target))
    {
        Console.Error.WriteLine("Specify a brief slug or 'all'.");
        return 1;
    }

    var briefs = ResolveBriefs(target);
    if (briefs.Count == 0)
    {
        Console.Error.WriteLine($"Unknown brief '{target}'. Try:  dotnet run -- list");
        return 1;
    }

    var models = (opts.GetValueOrDefault("models") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (models.Length == 0)
    {
        Console.Error.WriteLine("Pass --models a,b,c (e.g. claude-opus-4-8,claude-sonnet-4-6,claude-haiku-4-5).");
        return 1;
    }

    var maxTokens = int.TryParse(opts.GetValueOrDefault("max-tokens"), out var mt) ? mt : 16000;
    var csvPath = Path.Combine(root, "benchmark-results.csv");
    Directory.CreateDirectory(root);
    EnsureCsvHeader(csvPath);

    // Sequential on purpose: clean per-call timing and gentle on rate limits.
    foreach (var brief in briefs)
    {
        foreach (var model in models)
        {
            Console.WriteLine($"\n→ {brief.Slug} × {model} …");
            IModelProvider provider;
            try { provider = BuildProvider(model); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

            // Per-model subdir so themes can be compared side by side.
            var dir = Path.Combine(root, brief.Slug, ModelSlug(model));
            var result = await GenerateOne(provider, model, brief, maxTokens, dir);
            PrintResult(result);
            AppendCsvRow(csvPath, brief.Slug, result);
        }
    }

    Console.WriteLine($"\nResults logged to {csvPath}");
    Console.WriteLine("Preview a specific model with:  dotnet run -- serve <slug> --model <id>");
    return 0;
}

async Task<GenResult> GenerateOne(IModelProvider provider, string model, CustomerBrief brief, int maxTokens, string outputDir)
{
    var request = new ModelRequest
    {
        AgentName = "ThemeHarness",
        TaskType = "tier1-marketing-ui",
        SystemPrompt = ThemePrompt.System,
        UserPrompt = ThemePrompt.BuildUser(brief),
        MaxTokens = maxTokens,
    };

    var sw = Stopwatch.StartNew();
    try
    {
        var response = await provider.CompleteAsync(request, CancellationToken.None);
        sw.Stop();

        var inputTokens = ToLong(response.Usage.GetValueOrDefault("input_tokens"));
        var outputTokens = ToLong(response.Usage.GetValueOrDefault("output_tokens"));
        var cost = Pricing.Cost(model, inputTokens, outputTokens);

        var files = SiteWriter.Parse(response.ResponseText);
        if (files.Count == 0)
        {
            Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "_raw.txt"), response.ResponseText);
            return new GenResult(model, outputDir, false, inputTokens, outputTokens, cost, sw.Elapsed.TotalSeconds,
                response.WasTruncated, 0, "no parseable files (raw saved to _raw.txt)");
        }

        SiteWriter.Write(outputDir, files);
        return new GenResult(model, outputDir, true, inputTokens, outputTokens, cost, sw.Elapsed.TotalSeconds,
            response.WasTruncated, files.Count, null);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new GenResult(model, outputDir, false, 0, 0, null, sw.Elapsed.TotalSeconds, false, 0, ex.Message);
    }
}

void PrintResult(GenResult r)
{
    var cost = r.CostUsd is { } c ? $"${c:0.0000}" : "$?";
    var status = r.Ok ? "✓" : "✗";
    var detail = r.Error is { } e ? $"  — {e}" : "";
    var trunc = r.WasTruncated ? "  ⚠ TRUNCATED (raise --max-tokens)" : "";
    Console.WriteLine($"  {status} {r.Seconds:0.0}s  in={r.InputTokens} out={r.OutputTokens}  {cost}  files={r.Files}{trunc}{detail}");
    if (r.Ok) Console.WriteLine($"    {r.OutputDir}");
}

async Task<int> Serve(string? slug, IReadOnlyDictionary<string, string> opts, string root)
{
    if (string.IsNullOrWhiteSpace(slug))
    {
        Console.Error.WriteLine("Specify a slug to serve, e.g.  dotnet run -- serve brightsmile-dental");
        return 1;
    }

    var modelOpt = opts.GetValueOrDefault("model");
    var dir = Path.GetFullPath(modelOpt is null
        ? Path.Combine(root, slug)
        : Path.Combine(root, slug, ModelSlug(modelOpt)));

    if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "index.html")))
    {
        Console.Error.WriteLine($"No generated site at {dir}. Run:  dotnet run -- generate {slug}");
        return 1;
    }

    var port = int.TryParse(opts.GetValueOrDefault("port"), out var p) ? p : 5443;

    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls($"https://localhost:{port}");
    var app = builder.Build();

    var fileProvider = new PhysicalFileProvider(dir);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

    Console.WriteLine($"Serving {dir} at https://localhost:{port}   (Ctrl+C to stop)");
    Console.WriteLine("If the browser warns about the certificate, run once:  dotnet dev-certs https --trust");
    await app.RunAsync();
    return 0;
}

IModelProvider BuildProvider(string model)
{
    var apiKey = Environment.GetEnvironmentVariable("AnthropicApiKey")
        ?? throw new InvalidOperationException("AnthropicApiKey environment variable is not set.");

    var http = new HttpClient
    {
        BaseAddress = new Uri("https://api.anthropic.com/v1/"),
        Timeout = TimeSpan.FromMinutes(5),
    };
    http.DefaultRequestHeaders.Add("x-api-key", apiKey);
    http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

    var providerOptions = new ModelProviderOptions
    {
        ProviderName = "Anthropic",
        ModelName = model,
        DefaultMaxTokens = 16000,
    };

    var rateLimiter = new AnthropicRateLimiter(new AnthropicRateLimiterOptions { MaxConcurrentRequests = 2 });
    return new AnthropicModelProvider(http, providerOptions, new RegexRedactionService(), rateLimiter);
}

static long ToLong(object? value) => value switch
{
    null => 0,
    long l => l,
    int i => i,
    _ => long.TryParse(value.ToString(), out var n) ? n : 0,
};

static string ModelSlug(string model) => model.Replace('.', '-').Replace('/', '-');

static void EnsureCsvHeader(string csvPath)
{
    if (File.Exists(csvPath)) return;
    File.WriteAllText(csvPath, "timestamp_utc,slug,model,input_tokens,output_tokens,cost_usd,seconds,truncated,files,ok,error\n");
}

static void AppendCsvRow(string csvPath, string slug, GenResult r)
{
    static string Esc(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    var ts = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    var cost = r.CostUsd?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "";
    var row = new StringBuilder()
        .Append(ts).Append(',')
        .Append(Esc(slug)).Append(',')
        .Append(Esc(r.Model)).Append(',')
        .Append(r.InputTokens).Append(',')
        .Append(r.OutputTokens).Append(',')
        .Append(cost).Append(',')
        .Append(r.Seconds.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
        .Append(r.WasTruncated).Append(',')
        .Append(r.Files).Append(',')
        .Append(r.Ok).Append(',')
        .Append(Esc(r.Error ?? ""))
        .Append('\n');
    File.AppendAllText(csvPath, row.ToString());
}

static (string? Command, string? Positional, Dictionary<string, string> Options) ParseArgs(string[] args)
{
    string? command = null;
    string? positional = null;
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (a.StartsWith("--"))
        {
            var key = a[2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
            options[key] = value;
        }
        else if (command is null) command = a;
        else positional ??= a;
    }

    return (command, positional, options);
}

internal sealed record GenResult(
    string Model,
    string OutputDir,
    bool Ok,
    long InputTokens,
    long OutputTokens,
    decimal? CostUsd,
    double Seconds,
    bool WasTruncated,
    int Files,
    string? Error);
