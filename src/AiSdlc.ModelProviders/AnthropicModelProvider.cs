using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.Shared.Redaction;

namespace AiSdlc.ModelProviders;

public sealed class AnthropicModelProvider : IModelProvider
{
    private readonly HttpClient _http;
    private readonly ModelProviderOptions _options;
    private readonly IRedactionService _redaction;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicModelProvider(HttpClient http, ModelProviderOptions options, IRedactionService redaction)
    {
        _http      = http;
        _options   = options;
        _redaction = redaction;
    }

    public string ProviderName => "Anthropic";

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var systemPrompt = _redaction.Redact(request.SystemPrompt ?? string.Empty).RedactedText;

        var body = new
        {
            Model     = _options.ModelName,
            MaxTokens = request.MaxTokens ?? _options.DefaultMaxTokens,
            System    = systemPrompt,
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

    private string BuildUserContent(ModelRequest request)
    {
        var userPrompt = _redaction.Redact(request.UserPrompt).RedactedText;

        if (request.ContextDocuments.Count == 0)
            return userPrompt;

        var sb = new StringBuilder();
        foreach (var (name, content) in request.ContextDocuments)
        {
            sb.AppendLine($"<document name=\"{name}\">");
            sb.AppendLine(_redaction.Redact(content).RedactedText);
            sb.AppendLine("</document>");
            sb.AppendLine();
        }
        sb.Append(userPrompt);
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
