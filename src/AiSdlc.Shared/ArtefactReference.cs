namespace AiSdlc.Shared;

/// <summary>
/// Represents a generated or referenced artefact produced during the workflow.
/// </summary>
public sealed record ArtefactReference(
    string Name,
    string Type,
    string Location,
    string? ContentHash = null);
