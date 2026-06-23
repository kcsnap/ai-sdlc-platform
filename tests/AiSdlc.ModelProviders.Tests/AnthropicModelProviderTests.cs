using System.Net;
using System.Text;
using AiSdlc.ModelProviders;
using AiSdlc.Shared.Redaction;
using Xunit;

namespace AiSdlc.ModelProviders.Tests;

public sealed class AnthropicModelProviderTests
{
    private static readonly ModelProviderOptions Options = new()
    {
        ProviderName     = "Anthropic",
        ModelName        = "claude-haiku-4-5-20251001",
        DefaultMaxTokens = 1024
    };

    private static readonly ModelRequest SampleRequest = new()
    {
        AgentName    = "ProductStrategist",
        TaskType     = "brief",
        SystemPrompt = "You are a helpful assistant.",
        UserPrompt   = "Summarise this issue."
    };

    private static readonly string SuccessBody = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001",
         "stop_reason":"end_turn","content":[{"type":"text","text":"Here is the summary."}],
         "usage":{"input_tokens":10,"output_tokens":5}}
        """;

    [Fact]
    public async Task CompleteAsync_SuccessOnFirstAttempt_ReturnsResponse()
    {
        var handler  = new SequentialHandler([(HttpStatusCode.OK, SuccessBody)]);
        var provider = MakeProvider(handler);

        var result = await provider.CompleteAsync(SampleRequest, CancellationToken.None);

        Assert.Equal("Here is the summary.", result.ResponseText);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_OneRetry_SucceedsOnSecondAttempt()
    {
        var handler = new SequentialHandler(
        [
            (HttpStatusCode.TooManyRequests, "{\"error\":{\"type\":\"rate_limit_error\"}}"),
            (HttpStatusCode.OK, SuccessBody)
        ]);
        var provider = MakeProvider(handler);

        var result = await provider.CompleteAsync(SampleRequest, CancellationToken.None);

        Assert.Equal("Here is the summary.", result.ResponseText);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_BadRequest_SurfacesErrorBodyInException()
    {
        // The Anthropic 400 body carries the actionable detail (v17 incident: credit
        // exhaustion was invisible because only the bare status code was surfaced).
        var errorBody = """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API."}}""";
        var handler   = new SequentialHandler([(HttpStatusCode.BadRequest, errorBody)]);
        var provider  = MakeProvider(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.CompleteAsync(SampleRequest, CancellationToken.None));

        Assert.Contains("400", ex.Message);
        Assert.Contains("credit balance is too low", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_AllAttemptsReturn429_ThrowsHttpRequestException()
    {
        var responses = Enumerable.Repeat(
            (HttpStatusCode.TooManyRequests, "{\"error\":{\"type\":\"rate_limit_error\"}}"), 5).ToList();
        var handler  = new SequentialHandler(responses);
        var provider = MakeProvider(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.CompleteAsync(SampleRequest, CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_UsesPerAgentOverrideModel_WhenAgentIsMapped()
    {
        var options = Options with
        {
            ModelOverridesByAgent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code Implementer"] = "claude-opus-4-8"
            }
        };
        var handler  = new CapturingHandler(SuccessBody);
        var provider = MakeProvider(handler, options);

        await provider.CompleteAsync(SampleRequest with { AgentName = "Code Implementer" }, CancellationToken.None);

        Assert.Contains("\"model\":\"claude-opus-4-8\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_UsesGlobalModel_WhenAgentNotMapped()
    {
        var options = Options with
        {
            ModelOverridesByAgent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code Implementer"] = "claude-opus-4-8"
            }
        };
        var handler  = new CapturingHandler(SuccessBody);
        var provider = MakeProvider(handler, options);

        await provider.CompleteAsync(SampleRequest with { AgentName = "Product Strategist" }, CancellationToken.None);

        Assert.Contains("\"model\":\"claude-haiku-4-5-20251001\"", handler.LastRequestBody);
    }

    private static AnthropicModelProvider MakeProvider(HttpMessageHandler handler) => MakeProvider(handler, Options);

    private static AnthropicModelProvider MakeProvider(HttpMessageHandler handler, ModelProviderOptions options)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        return new AnthropicModelProvider(http, options, new NoOpRedactionService(),
            new AnthropicRateLimiter(new AnthropicRateLimiterOptions()));
    }

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SequentialHandler(List<(HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
    {
        private int _index;
        public int CallCount => _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = responses[Math.Min(_index++, responses.Count - 1)];
            var msg = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (status == HttpStatusCode.TooManyRequests)
                msg.Headers.Add("retry-after", "0"); // avoid real waits in tests
            return Task.FromResult(msg);
        }
    }

    private sealed class NoOpRedactionService : IRedactionService
    {
        public RedactionResult Redact(string input) => new() { RedactedText = input, RedactionCount = 0 };
    }
}
