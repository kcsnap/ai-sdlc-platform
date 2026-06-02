namespace AiSdlc.Events.Contract;

/// <summary>
/// Forward-compatibility fallback. When the contract package encounters an <c>eventType</c> it does not recognize
/// (typically because the producer is a newer contract version), it deserializes the payload as <see cref="UnknownEventData"/>
/// instead of throwing. Consumers can detect-and-handle with a type check.
/// </summary>
/// <param name="OriginalEventType">The unrecognized <c>eventType</c> string from the envelope.</param>
/// <param name="RawDataJson">The raw JSON of the <c>data</c> property, untouched, for forward-compatible inspection or logging.</param>
public sealed record UnknownEventData(
    string OriginalEventType,
    string RawDataJson) : EventData;
