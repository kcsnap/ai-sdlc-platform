using System.Text.Json;
using AiSdlc.Events.Contract;
using Xunit;

namespace AiSdlc.Events.Contract.Tests;

public sealed class ForwardCompatibilityTests
{
    private static readonly JsonSerializerOptions Options = EventStreamSerializer.Options;

    [Fact]
    public void UnknownEventType_DeserializesToUnknownEventData_WithoutThrowing()
    {
        const string json = """
        {
          "cursor": "cursor-1",
          "runId": "kcsnap_ai-sdlc-platform_123",
          "occurredAt": "2026-06-02T14:32:11.234Z",
          "eventType": "FutureEventTypeFromV2",
          "repository": "kcsnap/ai-sdlc-platform",
          "issueNumber": 123,
          "redactionApplied": false,
          "data": { "newField": "newValue", "anotherField": 42 }
        }
        """;

        var envelope = JsonSerializer.Deserialize<EventEnvelope>(json, Options);

        Assert.NotNull(envelope);
        Assert.Equal(EventType.Unknown, envelope!.EventType);
        var unknown = Assert.IsType<UnknownEventData>(envelope.Data);
        Assert.Equal("FutureEventTypeFromV2", unknown.OriginalEventType);
        Assert.Contains("\"newField\"", unknown.RawDataJson);
        Assert.Contains("\"anotherField\"", unknown.RawDataJson);
    }

    [Fact]
    public void UnknownEventType_RoundTripsWithOriginalDiscriminator()
    {
        const string originalJson = """
        {
          "cursor": "cursor-1",
          "runId": "kcsnap_ai-sdlc-platform_123",
          "occurredAt": "2026-06-02T14:32:11.234Z",
          "eventType": "FutureEventTypeFromV2",
          "repository": "kcsnap/ai-sdlc-platform",
          "issueNumber": 123,
          "redactionApplied": false,
          "data": { "newField": "newValue" }
        }
        """;

        var envelope = JsonSerializer.Deserialize<EventEnvelope>(originalJson, Options)!;
        var reserialized = JsonSerializer.Serialize(envelope, Options);

        using var doc = JsonDocument.Parse(reserialized);
        Assert.Equal("FutureEventTypeFromV2", doc.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("newValue", doc.RootElement.GetProperty("data").GetProperty("newField").GetString());
    }

    [Fact]
    public void MissingOptionalFields_DeserializeWithDefaults()
    {
        const string json = """
        {
          "cursor": "cursor-1",
          "runId": "kcsnap_ai-sdlc-platform_123",
          "occurredAt": "2026-06-02T14:32:11.234Z",
          "eventType": "WorkflowStarted",
          "repository": "kcsnap/ai-sdlc-platform",
          "issueNumber": 123,
          "data": {}
        }
        """;

        var envelope = JsonSerializer.Deserialize<EventEnvelope>(json, Options);

        Assert.NotNull(envelope);
        Assert.Null(envelope!.PullRequestNumber);
        Assert.False(envelope.RedactionApplied);
    }
}
