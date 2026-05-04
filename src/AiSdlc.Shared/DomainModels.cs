namespace AiSdlc.Shared;

/// <summary>
/// Represents the current lifecycle state of an AI SDLC workflow run.
/// </summary>
public enum WorkflowRunStatus
{
    Started,
    AwaitingClarification,
    BriefReady,
    AwaitingBriefApproval,
    BriefApproved,
    Analysing,
    Implementing,
    PullRequestOpen,
    Reviewing,
    RiskAssessing,
    AwaitingHumanReview,
    ReadyToRelease,
    Deploying,
    Released,
    Stopped,
    Failed
}

/// <summary>
/// Represents the assessed risk level for a change.
/// </summary>
public enum RiskLevel
{
    Unknown,
    Low,
    Medium,
    High
}

/// <summary>
/// Represents the decision made by the risk assessment stage.
/// </summary>
public enum RiskDecision
{
    Unknown,
    ContinueAutonomously,
    RequireHumanReview,
    StopWorkflow
}

/// <summary>
/// Represents the GitHub issue that initiated or is associated with a workflow run.
/// </summary>
public sealed record GitHubIssueReference(
    string Repository,
    int IssueNumber,
    string Url);

/// <summary>
/// Represents a GitHub pull request created or reviewed by the workflow.
/// </summary>
public sealed record GitHubPullRequestReference(
    string Repository,
    int PullRequestNumber,
    string BranchName,
    string Url);

/// <summary>
/// Represents a generated or referenced artefact produced during the workflow.
/// </summary>
public sealed record ArtefactReference(
    string Name,
    string Type,
    string Location,
    string? ContentHash = null);

/// <summary>
/// Context passed to an AI SDLC persona/activity function.
/// </summary>
public sealed record AgentContext
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
    /// GitHub issue number associated with the run.
    /// </summary>
    public required int IssueNumber { get; init; }

    /// <summary>
    /// Optional pull request number associated with the run.
    /// </summary>
    public int? PullRequestNumber { get; init; }

    /// <summary>
    /// Current workflow state when the agent is invoked.
    /// </summary>
    public required WorkflowRunStatus CurrentState { get; init; }

    /// <summary>
    /// Name of the persona/agent being requested.
    /// </summary>
    public required string RequestedAgent { get; init; }

    /// <summary>
    /// Artefacts available to this agent.
    /// </summary>
    public IReadOnlyDictionary<string, ArtefactReference> Artefacts { get; init; } = new Dictionary<string, ArtefactReference>();

    /// <summary>
    /// Additional run metadata available to this agent.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Standard result returned by an AI SDLC persona/activity function.
/// </summary>
public sealed record AgentResult
{
    /// <summary>
    /// Name of the persona/agent that produced the result.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Result status, such as Completed, NeedsClarification, Blocked or Failed.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable summary of the agent output.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Optional decision made by the agent.
    /// </summary>
    public string? Decision { get; init; }

    /// <summary>
    /// Optional risk level identified by the agent.
    /// </summary>
    public RiskLevel? RiskLevel { get; init; }

    /// <summary>
    /// Artefacts created by this agent.
    /// </summary>
    public IReadOnlyList<ArtefactReference> ArtefactsCreated { get; init; } = Array.Empty<ArtefactReference>();

    /// <summary>
    /// Follow-up questions that should be asked before continuing.
    /// </summary>
    public IReadOnlyList<string> FollowUpQuestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Blocking issues that prevent the workflow from continuing.
    /// </summary>
    public IReadOnlyList<string> BlockingIssues { get; init; } = Array.Empty<string>();
}

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
    public RiskDecision RiskDecision { get; init; } = RiskDecision.Unknown;

    /// <summary>
    /// Artefacts produced or referenced by the run.
    /// </summary>
    public IReadOnlyList<ArtefactReference> Artefacts { get; init; } = Array.Empty<ArtefactReference>();
}

/// <summary>
/// Immutable audit event written by the AI SDLC platform.
/// </summary>
public sealed record AuditEvent
{
    /// <summary>
    /// Unique workflow run identifier.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Time the event occurred.
    /// </summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// Repository in owner/name format.
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// GitHub issue number associated with the event.
    /// </summary>
    public required int IssueNumber { get; init; }

    /// <summary>
    /// Optional pull request number associated with the event.
    /// </summary>
    public int? PullRequestNumber { get; init; }

    /// <summary>
    /// Actor type, such as Agent, Human, System or GitHubAction.
    /// </summary>
    public required string ActorType { get; init; }

    /// <summary>
    /// Actor name, such as ProductStrategist, RiskAssessor or a GitHub username.
    /// </summary>
    public required string ActorName { get; init; }

    /// <summary>
    /// Action performed.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Human-readable event summary.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Optional decision captured by the event.
    /// </summary>
    public string? Decision { get; init; }

    /// <summary>
    /// Optional risk level captured by the event.
    /// </summary>
    public RiskLevel? RiskLevel { get; init; }

    /// <summary>
    /// Optional commit SHA associated with the event.
    /// </summary>
    public string? CommitSha { get; init; }

    /// <summary>
    /// Indicates whether PII or secrets redaction was applied before storing details.
    /// </summary>
    public bool RedactionApplied { get; init; }

    /// <summary>
    /// External references, such as artefact IDs, check run IDs or deployment IDs.
    /// </summary>
    public IReadOnlyDictionary<string, string> References { get; init; } = new Dictionary<string, string>();
}
