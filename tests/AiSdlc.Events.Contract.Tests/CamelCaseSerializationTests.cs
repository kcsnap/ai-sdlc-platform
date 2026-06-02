using System.Text.Json;
using AiSdlc.Events.Contract;
using AiSdlc.Events.Contract.Data;
using Xunit;

namespace AiSdlc.Events.Contract.Tests;

public sealed class CamelCaseSerializationTests
{
    private static readonly JsonSerializerOptions Options = EventStreamSerializer.Options;

    [Fact]
    public void EnvelopeProperties_AreCamelCaseInJson()
    {
        var envelope = new EventEnvelope
        {
            Cursor = "c1",
            RunId = "kcsnap_ai-sdlc-platform_123",
            OccurredAt = new DateTimeOffset(2026, 6, 2, 14, 32, 11, 234, TimeSpan.Zero),
            EventType = EventType.AgentStarted,
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 123,
            PullRequestNumber = 124,
            RedactionApplied = false,
            Data = new AgentStartedData("Architect"),
        };

        var json = JsonSerializer.Serialize(envelope, Options);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("cursor", out _));
        Assert.True(root.TryGetProperty("runId", out _));
        Assert.True(root.TryGetProperty("occurredAt", out _));
        Assert.True(root.TryGetProperty("eventType", out _));
        Assert.True(root.TryGetProperty("repository", out _));
        Assert.True(root.TryGetProperty("issueNumber", out _));
        Assert.True(root.TryGetProperty("pullRequestNumber", out _));
        Assert.True(root.TryGetProperty("redactionApplied", out _));
        Assert.True(root.TryGetProperty("data", out _));
    }

    [Fact]
    public void DataProperties_AreCamelCaseInJson()
    {
        var envelope = new EventEnvelope
        {
            Cursor = "c1",
            RunId = "r",
            OccurredAt = DateTimeOffset.UnixEpoch,
            EventType = EventType.AgentCompleted,
            Repository = "o/r",
            IssueNumber = 1,
            Data = new AgentCompletedData(
                AgentName: "X",
                Summary: "ok",
                Decision: "AUTO_MERGE_ELIGIBLE",
                RiskLevel: "Low",
                CommitSha: "deadbeef"),
        };

        var json = JsonSerializer.Serialize(envelope, Options);

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("X", data.GetProperty("agentName").GetString());
        Assert.Equal("ok", data.GetProperty("summary").GetString());
        Assert.Equal("AUTO_MERGE_ELIGIBLE", data.GetProperty("decision").GetString());
        Assert.Equal("Low", data.GetProperty("riskLevel").GetString());
        Assert.Equal("deadbeef", data.GetProperty("commitSha").GetString());
    }

    [Fact]
    public void EventType_SerializedAsPascalCaseString()
    {
        var envelope = new EventEnvelope
        {
            Cursor = "c",
            RunId = "r",
            OccurredAt = DateTimeOffset.UnixEpoch,
            EventType = EventType.BootstrapTerminalMarker,
            Repository = "o/r",
            IssueNumber = 1,
            Data = new BootstrapTerminalMarkerData("completed"),
        };

        var json = JsonSerializer.Serialize(envelope, Options);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("BootstrapTerminalMarker", doc.RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public void NullOptionalFields_AreOmittedFromJson()
    {
        var envelope = new EventEnvelope
        {
            Cursor = "c",
            RunId = "r",
            OccurredAt = DateTimeOffset.UnixEpoch,
            EventType = EventType.AgentStarted,
            Repository = "o/r",
            IssueNumber = 1,
            PullRequestNumber = null,
            Data = new AgentStartedData("Architect", Summary: null),
        };

        var json = JsonSerializer.Serialize(envelope, Options);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("pullRequestNumber", out _));
        Assert.False(doc.RootElement.GetProperty("data").TryGetProperty("summary", out _));
    }
}
