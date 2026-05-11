namespace AiSdlc.Shared.Redaction;

/// <summary>
/// Redacts secrets and PII from text before it is stored in audit logs or sent to model providers.
/// </summary>
public interface IRedactionService
{
    /// <summary>
    /// Returns a copy of <paramref name="input"/> with detected secrets and PII replaced by placeholders.
    /// </summary>
    RedactionResult Redact(string input);
}

public sealed record RedactionResult
{
    public required string RedactedText   { get; init; }
    public required int    RedactionCount { get; init; }
    public IReadOnlyList<string> RedactedPatterns { get; init; } = [];
}
