namespace AiSdlc.Events.Contract;

/// <summary>
/// Marker base type for the per-event-type <c>data</c> payload of an <see cref="EventEnvelope"/>.
/// Concrete subtypes live under the <c>Data</c> folder and map 1:1 to <see cref="EventType"/> members.
/// </summary>
public abstract record EventData;
