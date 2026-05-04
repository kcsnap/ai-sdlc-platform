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
