using System.Text.Json;
using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

/// <summary>
/// F9 (TDD red-first): the create-build intake accepts an OPTIONAL requested generation model —
/// flat "model" shorthand or a "models" object ({default, phases}). Unknown ids are a clear 400
/// (never a silent fallback); "phases" is FUTURE and must be ignored gracefully, never rejected.
/// </summary>
public sealed class CreateBuildModelSelectionTests
{
    private static string Body(string extraJson = "") => $$"""
        {
          "appId": "f9model0-test",
          "callbackBaseUrl": "https://cb.example/v1/admin",
          {{extraJson}}
          "charter": {
            "schemaVersion": 1,
            "identity": { "appName": "F9App", "oneLineDescription": "x" },
            "audience": { "primaryUserDescription": "x", "expectedScale": 0 },
            "purpose": { "problemBeingSolved": "x", "successCriteria": ["x"] },
            "features": [],
            "constraints": { "dataSensitivity": 0, "needsAuth": false, "needsPayments": false, "needsEmail": false, "needsAIApi": false, "needsPersistence": false },
            "integrations": [],
            "additionalContext": ""
          }
        }
        """;

    [Theory]
    [InlineData("claude-haiku-4-5")]
    [InlineData("claude-sonnet-4-5")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-opus-4-7")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-fable-5")]
    public void ParseAndValidate_accepts_every_allow_listed_model(string model)
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body($"\"model\": \"{model}\","));

        Assert.Null(error);
        Assert.Equal(model, request!.Model);
    }

    [Fact]
    public void ParseAndValidate_absent_model_stays_null_for_default_behavior()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body());

        Assert.Null(error);
        Assert.Null(request!.Model);
    }

    [Fact]
    public void ParseAndValidate_rejects_unknown_model_with_the_allowed_list()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body("\"model\": \"gpt-9-mega\","));

        Assert.Null(request);
        Assert.NotNull(error);
        Assert.Contains("gpt-9-mega", error);
        Assert.Contains("claude-fable-5", error); // the 400 names the legal values
    }

    [Fact]
    public void ParseAndValidate_normalizes_models_default_and_ignores_future_phases()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body(
            "\"models\": { \"default\": \"claude-opus-4-8\", \"phases\": { \"code-gen\": \"claude-fable-5\" } },"));

        Assert.Null(error);
        Assert.Equal("claude-opus-4-8", request!.Model); // normalized onto the flat field
    }

    // SEQ-177-AMEND wire pins: the settled canonical form is the "models" OBJECT. Explicit-null
    // object and explicit-null phases are both legal no-ops (their client sends a valid object or none).
    [Theory]
    [InlineData("\"models\": null,")]
    [InlineData("\"models\": { \"default\": null, \"phases\": null },")]
    public void ParseAndValidate_null_models_and_null_members_mean_default_behavior(string extra)
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body(extra));

        Assert.Null(error);
        Assert.Null(request!.Model);
    }

    [Fact]
    public void ParseAndValidate_models_default_with_null_phases_uses_the_default()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body(
            "\"models\": { \"default\": \"claude-fable-5\", \"phases\": null },"));

        Assert.Null(error);
        Assert.Equal("claude-fable-5", request!.Model);
    }

    [Fact]
    public void ParseAndValidate_rejects_conflicting_model_and_models_default()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body(
            "\"model\": \"claude-haiku-4-5\", \"models\": { \"default\": \"claude-opus-4-8\" },"));

        Assert.Null(request);
        Assert.Contains("conflict", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAndValidate_agreeing_model_and_models_default_is_fine()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(Body(
            "\"model\": \"claude-opus-4-8\", \"models\": { \"default\": \"claude-opus-4-8\" },"));

        Assert.Null(error);
        Assert.Equal("claude-opus-4-8", request!.Model);
    }

    // The activity reads the requested model from agent metadata — which Durable checkpointing
    // round-trips as JsonElement, exactly like the appId case (D3).
    [Fact]
    public void RequestedModel_metadata_reads_string_and_JsonElement_forms()
    {
        var meta = new Dictionary<string, object> { ["requestedModel"] = "claude-fable-5" };
        Assert.Equal("claude-fable-5", AgentActivityFunctions.RequestedModel(meta));

        meta["requestedModel"] = JsonSerializer.Deserialize<JsonElement>("\"claude-opus-4-8\"");
        Assert.Equal("claude-opus-4-8", AgentActivityFunctions.RequestedModel(meta));

        Assert.Null(AgentActivityFunctions.RequestedModel(new Dictionary<string, object>()));
    }
}
