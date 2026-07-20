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
    private readonly AnthropicRateLimiter _rateLimiter;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicModelProvider(
        HttpClient http, ModelProviderOptions options, IRedactionService redaction,
        AnthropicRateLimiter rateLimiter)
    {
        _http        = http;
        _options     = options;
        _redaction   = redaction;
        _rateLimiter = rateLimiter;
    }

    public string ProviderName => "Anthropic";

    private const int MaxRetries = 2;

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var systemPrompt = _redaction.Redact(request.SystemPrompt ?? string.Empty).RedactedText;
        var maxTokens    = request.MaxTokens ?? _options.DefaultMaxTokens;
        var model        = ResolveModel(request.AgentName);
        var caching      = _options.EnablePromptCaching;

        // The system prompt and the context-document prefix are stable across the many calls that
        // share them; the trailing user prompt (per-batch instructions, findings) is what varies.
        // A cache breakpoint after the system block and after the last document lets Anthropic reuse
        // that prefix at ~10% input cost. Disabled → the original single-string body, byte-for-byte.
        var systemField  = BuildSystemField(systemPrompt, caching);
        var contentField = BuildUserContent(request, caching, out var contentChars);

        var body = new
        {
            Model     = model,
            MaxTokens = maxTokens,
            System    = systemField,
            Messages  = new[] { new { Role = "user", Content = contentField } }
        };

        // Rough input estimate (~4 chars per token) is enough for admission control —
        // the response headers correct the budget to the server's real numbers.
        var estimatedInputTokens = (systemPrompt.Length + contentChars) / 4;
        using var lease = await _rateLimiter.AcquireAsync(estimatedInputTokens, maxTokens, cancellationToken);

        HttpResponseMessage httpResponse = null!;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            httpResponse = await _http.PostAsJsonAsync("messages", body, JsonOpts, cancellationToken);
            _rateLimiter.RecordResponse(httpResponse);

            if ((int)httpResponse.StatusCode != 429)
                break;

            if (attempt == MaxRetries)
                break; // let EnsureSuccessStatusCode throw below

            // Honour retry-after if Anthropic sends it; otherwise exponential backoff
            var retryAfter = httpResponse.Headers.TryGetValues("retry-after", out var vals)
                && int.TryParse(vals.FirstOrDefault(), out var secs)
                    ? secs
                    : (int)Math.Pow(3, attempt + 1) * 5; // 15s, 45s, 135s

            await Task.Delay(TimeSpan.FromSeconds(retryAfter), cancellationToken);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            // Anthropic puts the actionable detail (e.g. "credit balance is too low",
            // "max_tokens exceeds model limit") in the response body — surface it, or
            // failures show up in audit/logs as a bare status code.
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            var detail    = errorBody.Length > 500 ? errorBody[..500] : errorBody;
            throw new HttpRequestException(
                $"Anthropic API returned {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}): {detail}",
                inner: null,
                statusCode: httpResponse.StatusCode);
        }

        var result = await httpResponse.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOpts, cancellationToken)
            ?? throw new InvalidOperationException("Empty response from Anthropic API.");

        // F9: safety classifiers can decline (HTTP 200, stop_reason "refusal" — e.g. claude-fable-5).
        // Deterministic for the prompt+model, so it must surface as a distinct non-retryable failure.
        if (string.Equals(result.StopReason, "refusal", StringComparison.OrdinalIgnoreCase))
            throw new ModelRefusalException(
                $"The model ({result.Model}) refused to generate this content (stop_reason=refusal) — " +
                "the build cannot proceed with this charter/model combination. Try a different model or revise the charter.");

        var text = result.Content.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;

        return new ModelResponse
        {
            ProviderName = "Anthropic",
            ModelName    = result.Model,
            ResponseText = text,
            Usage = new Dictionary<string, object>
            {
                ["input_tokens"]       = result.Usage.InputTokens,
                ["output_tokens"]      = result.Usage.OutputTokens,
                ["cache_read_tokens"]  = result.Usage.CacheReadInputTokens ?? 0,
                ["cache_write_tokens"] = result.Usage.CacheCreationInputTokens ?? 0
            },
            WasTruncated = result.StopReason == "max_tokens"
        };
    }

    // F9 precedence: the build request's model (owner's explicit choice, ambient) wins over the
    // per-agent override (platform tuning), which falls back to the global model.
    private string ResolveModel(string? agentName)
    {
        if (ModelSelectionContext.RequestedModel is { Length: > 0 } requested)
            return requested;
        return agentName is { } name
            && _options.ModelOverridesByAgent.TryGetValue(name, out var overrideModel)
            && !string.IsNullOrWhiteSpace(overrideModel)
                ? overrideModel
                : _options.ModelName;
    }

    // Ephemeral marker placed on the last block of a cacheable prefix; everything up to and
    // including a marked block is cached. Anthropic allows up to four such breakpoints.
    private static object CacheControl => new { type = "ephemeral" };

    // System prompt: a single cached text block when caching is on (and non-empty — an empty text
    // block is rejected), otherwise the plain string the API also accepts.
    private static object BuildSystemField(string systemPrompt, bool caching) =>
        caching && systemPrompt.Length > 0
            ? new object[] { new { type = "text", text = systemPrompt, cache_control = (object?)CacheControl } }
            : systemPrompt;

    // User message content. Caching off → the original single concatenated string (byte-for-byte).
    // Caching on → one text block per context document with the breakpoint on the LAST document, so
    // the system + full document prefix is reused; the variable user prompt trails uncached.
    private object BuildUserContent(ModelRequest request, bool caching, out int charLength)
    {
        var userPrompt = _redaction.Redact(request.UserPrompt).RedactedText;

        if (!caching)
        {
            var s = BuildConcatenatedContent(request, userPrompt);
            charLength = s.Length;
            return s;
        }

        var docs   = request.ContextDocuments.ToList();
        var blocks = new List<object>(docs.Count + 1);
        var total  = 0;
        for (var i = 0; i < docs.Count; i++)
        {
            var text = $"<document name=\"{docs[i].Key}\">\n{_redaction.Redact(docs[i].Value).RedactedText}\n</document>\n";
            total += text.Length;
            var isLastDoc = i == docs.Count - 1;
            blocks.Add(new { type = "text", text, cache_control = isLastDoc ? (object?)CacheControl : null });
        }

        if (userPrompt.Length > 0)
        {
            total += userPrompt.Length;
            blocks.Add(new { type = "text", text = userPrompt, cache_control = (object?)null });
        }

        charLength = total;
        return blocks.ToArray();
    }

    private string BuildConcatenatedContent(ModelRequest request, string userPrompt)
    {
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
    private sealed record AnthropicUsage(
        int InputTokens, int OutputTokens, int? CacheReadInputTokens, int? CacheCreationInputTokens);
}
