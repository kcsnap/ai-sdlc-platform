namespace AiSdlc.GitHub;

/// <summary>One annotation from a check run (dotnet problem matchers emit these for compiler errors).</summary>
public sealed record CheckAnnotation(string Path, int StartLine, string Level, string Message);

/// <summary>
/// Everything extractable about one FAILED check run: annotations preferred, the Actions
/// job-log tail as fallback. A finding can carry neither (extraction degraded) — callers
/// treat that as non-actionable.
/// </summary>
public sealed record FailedCheckFinding(
    string CheckName,
    IReadOnlyList<CheckAnnotation> Annotations,
    string? LogTail);
