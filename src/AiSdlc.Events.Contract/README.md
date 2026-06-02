# AiSdlc.Events.Contract

Typed event-stream contract DTOs for the AI SDLC Platform's run-events API.

This package contains DTOs, enums, and a pre-configured `JsonSerializerOptions` helper — no transport code, no I/O, no behavior. Consumers add a `PackageReference` and get compile-time-typed access to the event stream:

```csharp
using AiSdlc.Events.Contract;
using System.Text.Json;

var options = EventStreamSerializer.Options;
var response = JsonSerializer.Deserialize<EventsResponse>(json, options);

foreach (var envelope in response!.Events)
{
    switch (envelope.Data)
    {
        case AgentCompletedData ac:
            Console.WriteLine($"{ac.AgentName} completed: {ac.Summary}");
            break;
        case BootstrapTerminalMarkerData btm:
            Console.WriteLine($"Run finished: {btm.Status}");
            break;
        case UnknownEventData unknown:
            Console.WriteLine($"Unknown event type '{unknown.EventType}' — upgrade the package");
            break;
    }
}
```

## Versioning

[SemVer](https://semver.org).

- **Major** — breaking schema change (envelope field renamed or removed, event type removed, `data` field removed or retyped).
- **Minor** — additive (new event type, new optional `data` field).
- **Patch** — non-breaking serialization tweak, documentation fix.

Recommend pinning with a floating minor: `Version="1.*"`.

## Forward compatibility

Unrecognized `eventType` values deserialize to `UnknownEventData` carrying the raw discriminator and JSON payload. Consumers can detect-and-handle without throwing, so a contract minor-version bump on the producer doesn't crash older consumers.

## See also

[ADR-0004 — Platform → Yorrixx Event-Stream API Contract](https://github.com/kcsnap/ai-sdlc-platform/blob/main/docs/adr/0004-event-stream-api.md)
