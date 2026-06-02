using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.Events.Contract.Data;

namespace AiSdlc.Events.Contract;

/// <summary>
/// Custom converter honoring ADR-0004's envelope shape: <c>eventType</c> lives on the envelope, not on the nested <c>data</c> object.
/// Unknown <c>eventType</c> values deserialize to <see cref="UnknownEventData"/> rather than throwing — preserving forward compatibility across minor contract version bumps.
/// </summary>
public sealed class EventEnvelopeJsonConverter : JsonConverter<EventEnvelope>
{
    /// <inheritdoc />
    public override EventEnvelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var eventTypeString = GetRequiredString(root, "eventType");
        var dataElement = GetRequiredElement(root, "data");

        var (eventType, data) = DeserializeData(eventTypeString, dataElement, options);

        return new EventEnvelope
        {
            Cursor = GetRequiredString(root, "cursor"),
            RunId = GetRequiredString(root, "runId"),
            OccurredAt = root.GetProperty("occurredAt").GetDateTimeOffset(),
            EventType = eventType,
            Repository = GetRequiredString(root, "repository"),
            IssueNumber = root.GetProperty("issueNumber").GetInt32(),
            PullRequestNumber = TryGetNullableInt(root, "pullRequestNumber"),
            RedactionApplied = root.TryGetProperty("redactionApplied", out var ra) && ra.GetBoolean(),
            Data = data,
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, EventEnvelope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("cursor", value.Cursor);
        writer.WriteString("runId", value.RunId);
        writer.WriteString("occurredAt", value.OccurredAt.ToString("O"));
        writer.WriteString("eventType", EventTypeToDiscriminator(value.EventType, value.Data));
        writer.WriteString("repository", value.Repository);
        writer.WriteNumber("issueNumber", value.IssueNumber);

        if (value.PullRequestNumber.HasValue)
        {
            writer.WriteNumber("pullRequestNumber", value.PullRequestNumber.Value);
        }

        writer.WriteBoolean("redactionApplied", value.RedactionApplied);

        writer.WritePropertyName("data");
        WriteData(writer, value.Data, options);

        writer.WriteEndObject();
    }

    private static (EventType eventType, EventData data) DeserializeData(
        string eventTypeString,
        JsonElement dataElement,
        JsonSerializerOptions options)
    {
        return eventTypeString switch
        {
            nameof(EventType.WebhookReceived) => (EventType.WebhookReceived, Deserialize<WebhookReceivedData>(dataElement, options)),
            nameof(EventType.WorkflowStarted) => (EventType.WorkflowStarted, Deserialize<WorkflowStartedData>(dataElement, options)),
            nameof(EventType.AgentStarted) => (EventType.AgentStarted, Deserialize<AgentStartedData>(dataElement, options)),
            nameof(EventType.AgentCompleted) => (EventType.AgentCompleted, Deserialize<AgentCompletedData>(dataElement, options)),
            nameof(EventType.AgentFailed) => (EventType.AgentFailed, Deserialize<AgentFailedData>(dataElement, options)),
            nameof(EventType.CommentPosted) => (EventType.CommentPosted, Deserialize<CommentPostedData>(dataElement, options)),
            nameof(EventType.WorkflowReleased) => (EventType.WorkflowReleased, Deserialize<WorkflowReleasedData>(dataElement, options)),
            nameof(EventType.WorkflowStopped) => (EventType.WorkflowStopped, Deserialize<WorkflowStoppedData>(dataElement, options)),
            nameof(EventType.WorkflowFailed) => (EventType.WorkflowFailed, Deserialize<WorkflowFailedData>(dataElement, options)),
            nameof(EventType.BootstrapTerminalMarker) => (EventType.BootstrapTerminalMarker, Deserialize<BootstrapTerminalMarkerData>(dataElement, options)),
            _ => (EventType.Unknown, new UnknownEventData(eventTypeString, dataElement.GetRawText())),
        };
    }

    private static void WriteData(Utf8JsonWriter writer, EventData data, JsonSerializerOptions options)
    {
        if (data is UnknownEventData unknown)
        {
            using var raw = JsonDocument.Parse(unknown.RawDataJson);
            raw.RootElement.WriteTo(writer);
            return;
        }

        JsonSerializer.Serialize(writer, data, data.GetType(), options);
    }

    private static string EventTypeToDiscriminator(EventType eventType, EventData data)
    {
        return eventType == EventType.Unknown && data is UnknownEventData unknown
            ? unknown.OriginalEventType
            : eventType.ToString();
    }

    private static T Deserialize<T>(JsonElement element, JsonSerializerOptions options) where T : EventData
    {
        var result = element.Deserialize<T>(options);
        return result ?? throw new JsonException($"Failed to deserialize {typeof(T).Name}.");
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Required string property '{propertyName}' missing or not a string.");
        }
        return prop.GetString() ?? throw new JsonException($"Property '{propertyName}' is null.");
    }

    private static JsonElement GetRequiredElement(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
        {
            throw new JsonException($"Required property '{propertyName}' missing.");
        }
        return prop;
    }

    private static int? TryGetNullableInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }
}
