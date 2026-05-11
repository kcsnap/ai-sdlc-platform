using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiSdlc.ModelProviders;

public sealed class AnthropicModelProvider : IModelProvider
{
    private readonly HttpClient _http;
    private readonly ModelProviderOptions _options;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicModelProvider(HttpClient http, ModelProviderOptions options)
    {
        _http    = http;
        _options = options;
    }

    public string ProviderName => "Anthropic";

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            Model     = _options.ModelName,
            MaxTokens = request.MaxTokens ?? _options.DefaultMaxTokens,
            System    = request.SystemPrompt,
            Messages  = new[] { new { Role = "user", Content = BuildUserContent(request) } }
        };

        var httpResponse = await _http.PostAsJsonAsync("messages", body, JsonOpts, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        var result = await httpResponse.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOpts, cancellationToken)
            ?? throw new InvalidOperationException("Empty response from Anthropic API.");

        var text = result.Content.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;

        return new ModelResponse
        {
            ProviderName = "Anthropic",
            ModelName    = result.Model,
            ResponseText = text,
            Usage = new Dictionary<string, object>
            {
                ["input_tokens"]  = result.Usage.InputTokens,
                ["output_tokens"] = result.Usage.OutputTokens
            },
            WasTruncated = result.StopReason == "max_tokens"
        };
    }

    private static string BuildUserContent(ModelRequest request)
    {
        if (request.ContextDocuments.Count == 0)
            return request.UserPrompt;

        var sb = new StringBuilder();
        foreach (var (name, content) in request.ContextDocuments)
        {
            sb.AppendLine($"<document name=\"{name}\">");
            sb.AppendLine(content);
            sb.AppendLine("</document>");
            sb.AppendLine();
        }
        sb.Append(request.UserPrompt);
        return sb.ToString();
    }

    private sealed record AnthropicResponse(
        string Model,
        string StopReason,
        List<AnthropicContent> Content,
        AnthropicUsage Usage);

    private sealed record AnthropicContent(string Type, string? Text);
    private sealed record AnthropicUsage(int InputTokens, int OutputTokens);
}
