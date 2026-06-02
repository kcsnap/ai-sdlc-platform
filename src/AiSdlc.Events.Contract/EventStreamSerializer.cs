using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiSdlc.Events.Contract;

/// <summary>
/// Pre-configured <see cref="JsonSerializerOptions"/> used by both producers and consumers of the event-stream contract.
/// Locked configuration: camelCase property naming, enums serialized as strings, indented output disabled, default to UTC ISO-8601 (round-trip "O") for timestamps.
/// </summary>
public static class EventStreamSerializer
{
    /// <summary>The single configured options instance. Safe to share across threads.</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }
}
