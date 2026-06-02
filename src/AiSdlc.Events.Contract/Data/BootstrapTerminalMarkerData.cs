namespace AiSdlc.Events.Contract.Data;

/// <summary>
/// Bootstrap-mode-only completion signal. Mirrors the invisible HTML-comment marker introduced in PR #51 — emitted alongside (not instead of) that marker until v2 deprecates it.
/// </summary>
/// <param name="Status">Either <c>completed</c> or <c>failed</c>.</param>
public sealed record BootstrapTerminalMarkerData(
    string Status) : EventData;
