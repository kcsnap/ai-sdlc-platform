namespace AiSdlc.Shared;

/// <summary>
/// Represents one AI SDLC workflow run.
/// </summary>
public sealed record WorkflowRun
{
    /// <summary>
    /// Unique workflow run identifier.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Repository in owner/name format.
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// GitHub issue associated with the run.
    /// </summary>
    public required GitHubIssueReference Issue { get; init; }

    /// <summary>
    /// Pull request associated with the run, if one has been created.
    /// </summary>
    public GitHubPullRequestReference? PullRequest { get; init; }

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    public required WorkflowRunStatus Status { get; init; }

    /// <summary>
    /// Time the run was created.
    /// </summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Last time the run changed.
    /// </summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>
    /// Current assessed risk level.
    /// </summary>
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Unknown;

    /// <summary>
    /// Current risk decision.
    /// </summary>
    public string RiskDecision { get; init; } = "Unknown";

    /// <summary>
    /// Artefacts produced or referenced by the run.
    /// </summary>
    public IReadOnlyList<ArtefactReference> Artefacts { get; init; } = Array.Empty<ArtefactReference>();
}
