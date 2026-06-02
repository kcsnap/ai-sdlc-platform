using System.Text.Json;
using AiSdlc.Events.Contract;
using AiSdlc.Events.Contract.Data;
using Xunit;

namespace AiSdlc.Events.Contract.Tests;

public sealed class EventsResponseRoundTripTests
{
    private static readonly JsonSerializerOptions Options = EventStreamSerializer.Options;

    [Fact]
    public void EmptyPage_RoundTrips()
    {
        var response = new EventsResponse(
            Events: Array.Empty<EventEnvelope>(),
            NextCursor: "input-cursor",
            HasMore: false);

        var roundTripped = Roundtrip(response);

        Assert.Empty(roundTripped.Events);
        Assert.Equal("input-cursor", roundTripped.NextCursor);
        Assert.False(roundTripped.HasMore);
    }

    [Fact]
    public void PageWithMixedEventTypes_RoundTrips()
    {
        var response = new EventsResponse(
            Events:
            [
                MakeEnvelope("c1", EventType.WorkflowStarted, new WorkflowStartedData()),
                MakeEnvelope("c2", EventType.AgentStarted, new AgentStartedData("Architect")),
                MakeEnvelope("c3", EventType.AgentCompleted,
                    new AgentCompletedData("Architect", "Design complete.", Decision: "ContinueAutonomously")),
                MakeEnvelope("c4", EventType.BootstrapTerminalMarker,
                    new BootstrapTerminalMarkerData("completed")),
            ],
            NextCursor: "c4",
            HasMore: true);

        var roundTripped = Roundtrip(response);

        Assert.Equal(4, roundTripped.Events.Count);
        Assert.IsType<WorkflowStartedData>(roundTripped.Events[0].Data);
        Assert.IsType<AgentStartedData>(roundTripped.Events[1].Data);
        Assert.IsType<AgentCompletedData>(roundTripped.Events[2].Data);
        Assert.IsType<BootstrapTerminalMarkerData>(roundTripped.Events[3].Data);
        Assert.Equal("c4", roundTripped.NextCursor);
        Assert.True(roundTripped.HasMore);
    }

    [Fact]
    public void Response_UsesCamelCasePropertyNames()
    {
        var response = new EventsResponse(Array.Empty<EventEnvelope>(), "x", false);

        var json = JsonSerializer.Serialize(response, Options);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("events", out _));
        Assert.True(doc.RootElement.TryGetProperty("nextCursor", out _));
        Assert.True(doc.RootElement.TryGetProperty("hasMore", out _));
    }

    private static EventsResponse Roundtrip(EventsResponse response)
    {
        var json = JsonSerializer.Serialize(response, Options);
        var result = JsonSerializer.Deserialize<EventsResponse>(json, Options);
        Assert.NotNull(result);
        return result!;
    }

    private static EventEnvelope MakeEnvelope(string cursor, EventType eventType, EventData data) => new()
    {
        Cursor = cursor,
        RunId = "kcsnap_ai-sdlc-platform_123",
        OccurredAt = new DateTimeOffset(2026, 6, 2, 14, 32, 11, 234, TimeSpan.Zero),
        EventType = eventType,
        Repository = "kcsnap/ai-sdlc-platform",
        IssueNumber = 123,
        Data = data,
    };
}
