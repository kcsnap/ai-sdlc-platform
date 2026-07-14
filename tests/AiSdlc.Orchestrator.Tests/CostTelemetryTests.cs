using System.Net;
using System.Text.Json;
using AiSdlc.Agents;
using AiSdlc.ModelProviders;
using AiSdlc.Orchestrator.Cost;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class CostTelemetryTests
{
    [Theory]
    [InlineData(AgentNames.CodeImplementer, "CodeImplementation", "code-gen")]
    [InlineData(AgentNames.CodeImplementer, "CodeImplementationManifest", "code-gen")]
    [InlineData(AgentNames.CodeImplementer, "CodeRepair", "fix-loop")]
    [InlineData(AgentNames.QaTestEngineer, "x", "test-impl")]
    [InlineData(AgentNames.ProductStrategist, "x", "brief")]
    [InlineData(AgentNames.Architect, "x", "review")]
    [InlineData(AgentNames.ReleaseManager, "x", "other")]
    public void CostPhase_maps_agents_to_buckets(string agent, string task, string expected)
        => Assert.Equal(expected, CostPhase.For(agent, task));

    [Fact]
    public void BuildCostScope_extracts_appId_and_iteration()
    {
        var ctx = MakeContext();
        Assert.Equal("b39b1f7f", AgentActivityFunctions.BuildCostScope(ctx).AppId);
        Assert.Equal(0, AgentActivityFunctions.BuildCostScope(ctx).Iteration);

        ctx.Metadata["reopened"] = "true";
        Assert.Equal(1, AgentActivityFunctions.BuildCostScope(ctx).Iteration);
    }

    // Yorrixx keys /apps/{appId}/cost by the FULL 32-char id; repo names only carry appId8 (G6 P2),
    // so the new build path stamps the full id into metadata — it must win over the repo-name fallback,
    // including after Durable checkpointing turns metadata strings into JsonElement.
    [Fact]
    public void BuildCostScope_prefers_full_appId_from_metadata()
    {
        var ctx = MakeContext();
        ctx.Metadata["appId"] = "b39b1f7f49c14c02a3a9777a800cc44d";
        Assert.Equal("b39b1f7f49c14c02a3a9777a800cc44d", AgentActivityFunctions.BuildCostScope(ctx).AppId);

        ctx.Metadata["appId"] = JsonSerializer.Deserialize<JsonElement>("\"c50cb42440c2462a93a9777a800cc44d\"");
        Assert.Equal("c50cb42440c2462a93a9777a800cc44d", AgentActivityFunctions.BuildCostScope(ctx).AppId);
    }

    [Fact]
    public async Task Emits_cost_with_usage_phase_and_requestId()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var decorator = new CostEmittingModelProvider(
            new StubProvider(100, 50, 120000, 8000, "claude-opus-4-8"),
            new StubFactory(handler), NullLogger<CostEmittingModelProvider>.Instance,
            "https://yorrixx.test/", "admin-key");

        BuildCostContext.Current = new CostScope("b39b1f7f", 0);
        try
        {
            await decorator.CompleteAsync(
                new ModelRequest { AgentName = AgentNames.CodeImplementer, TaskType = "CodeImplementation", SystemPrompt = "s", UserPrompt = "x" },
                CancellationToken.None);
        }
        finally { BuildCostContext.Current = null; }

        Assert.Equal("/v1/admin/apps/b39b1f7f/cost", handler.LastPath);
        Assert.Equal("admin-key", handler.LastAuth);
        var body = JsonDocument.Parse(handler.LastBody).RootElement;
        Assert.Equal("claude-opus-4-8", body.GetProperty("model").GetString());
        Assert.Equal("code-gen", body.GetProperty("phase").GetString());
        Assert.Equal(100, body.GetProperty("inputTokens").GetInt64());
        Assert.Equal(50, body.GetProperty("outputTokens").GetInt64());
        Assert.Equal(120000, body.GetProperty("cacheReadTokens").GetInt64());
        Assert.Equal(8000, body.GetProperty("cacheWriteTokens").GetInt64());
        Assert.StartsWith("b39b1f7f:code-gen:0:", body.GetProperty("requestId").GetString());
    }

    // D3: the RequestId used an in-process counter, so a re-kicked build in a fresh worker regenerated
    // the previous run's exact idempotency keys and Yorrixx silently deduped all 21 emits ($0.00 delta
    // on 13 min of repair). Every emit is one real billed call and the POST is never retried — the key
    // must be unique per emit, across runs AND process restarts.
    [Fact]
    public async Task RequestId_is_unique_across_process_restarts_and_calls()
    {
        var ids = new List<string?>();
        for (var restart = 0; restart < 2; restart++) // fresh decorator instance = simulated worker restart
        {
            var handler = new CaptureHandler(HttpStatusCode.OK);
            var decorator = new CostEmittingModelProvider(
                new StubProvider(1, 1, 0, 0, "m"), new StubFactory(handler),
                NullLogger<CostEmittingModelProvider>.Instance, "https://yorrixx.test/", "k");

            BuildCostContext.Current = new CostScope("3e14295bae934e04b2b4922e5822b28f", 0);
            try
            {
                for (var call = 0; call < 2; call++)
                {
                    await decorator.CompleteAsync(
                        new ModelRequest { AgentName = AgentNames.CodeImplementer, TaskType = "CodeRepair", SystemPrompt = "s", UserPrompt = "x" },
                        CancellationToken.None);
                    ids.Add(JsonDocument.Parse(handler.LastBody).RootElement.GetProperty("requestId").GetString());
                }
            }
            finally { BuildCostContext.Current = null; }
        }

        Assert.Equal(4, ids.Count);
        Assert.Equal(4, ids.Distinct().Count()); // no collisions between calls OR "restarts"
        Assert.All(ids, id => Assert.StartsWith("3e14295b:fix-loop:0:", id)); // readable prefix retained
    }

    // Cost net: an LLM call reaching the provider with no attribution scope means an unwired
    // orchestration path — cost is lost, so it must be LOUD, and nothing must be POSTed.
    [Fact]
    public async Task Null_scope_logs_unwired_path_warning_and_does_not_post()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var log = new CapturingLogger();
        var decorator = new CostEmittingModelProvider(
            new StubProvider(1, 1, 0, 0, "m"), new StubFactory(handler), log, "https://yorrixx.test/", "k");

        BuildCostContext.Current = null;
        await decorator.CompleteAsync(new ModelRequest { AgentName = "x", TaskType = "x", SystemPrompt = "s", UserPrompt = "x" }, CancellationToken.None);

        Assert.Null(handler.LastPath);
        Assert.Contains(log.Warnings, w => w.Contains("no BuildCostContext"));
    }

    // Cost net: unconfigured telemetry warned about ONCE at boot (YorrixxApiBase sat unset from #185
    // until w1proof2 — an empty benchmark was the only symptom).
    [Fact]
    public void Unconfigured_emitter_warns_loudly_at_construction()
    {
        var log = new CapturingLogger();
        _ = new CostEmittingModelProvider(
            new StubProvider(1, 1, 0, 0, "m"), new StubFactory(new CaptureHandler(HttpStatusCode.OK)),
            log, yorrixxApiBase: null, yorrixxAdminKey: "k");

        Assert.Contains(log.Warnings, w => w.Contains("INERT"));
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<CostEmittingModelProvider>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task Does_not_emit_when_unconfigured()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var decorator = new CostEmittingModelProvider(
            new StubProvider(1, 1, 0, 0, "m"), new StubFactory(handler),
            NullLogger<CostEmittingModelProvider>.Instance, yorrixxApiBase: null, yorrixxAdminKey: null);

        BuildCostContext.Current = new CostScope("app", 0);
        try { await decorator.CompleteAsync(new ModelRequest { AgentName = "x", TaskType = "x", SystemPrompt = "s", UserPrompt = "x" }, CancellationToken.None); }
        finally { BuildCostContext.Current = null; }

        Assert.Null(handler.LastPath); // never POSTed
    }

    [Fact]
    public async Task Best_effort_swallows_post_failure()
    {
        var handler = new CaptureHandler(HttpStatusCode.InternalServerError);
        var decorator = new CostEmittingModelProvider(
            new StubProvider(1, 1, 0, 0, "m"), new StubFactory(handler),
            NullLogger<CostEmittingModelProvider>.Instance, "https://yorrixx.test/", "k");

        BuildCostContext.Current = new CostScope("app2", 0);
        try
        {
            var r = await decorator.CompleteAsync(new ModelRequest { AgentName = "x", TaskType = "x", SystemPrompt = "s", UserPrompt = "x" }, CancellationToken.None);
            Assert.NotNull(r); // returns the inner response despite the 500 — never throws
        }
        finally { BuildCostContext.Current = null; }
    }

    private static AgentContext MakeContext() => new()
    {
        RunId = "r", Repository = "yorrixx-apps/user-app-b39b1f7f", IssueNumber = 1,
        CurrentState = "x", RequestedAgent = "x"
    };

    private sealed class StubProvider(long input, long output, long cacheRead, long cacheWrite, string model) : IModelProvider
    {
        public string ProviderName => "Anthropic";
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelResponse
            {
                ProviderName = "Anthropic",
                ModelName = model,
                ResponseText = "x",
                Usage = new Dictionary<string, object>
                {
                    ["input_tokens"] = input, ["output_tokens"] = output,
                    ["cache_read_tokens"] = cacheRead, ["cache_write_tokens"] = cacheWrite
                },
                WasTruncated = false,
                Warnings = new List<string>()
            });
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://unused.test") };
    }

    private sealed class CaptureHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? LastPath { get; private set; }
        public string? LastAuth { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            LastAuth = request.Headers.TryGetValues("X-Yorrixx-Admin-Key", out var v) ? v.FirstOrDefault() : null;
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
