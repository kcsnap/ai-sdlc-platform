namespace AiSdlc.Shared;

/// <summary>
/// External event names raised on running orchestration instances.
/// Must match the event names raised by the webhook handler.
/// </summary>
public static class WorkflowEventNames
{
    public const string ApproveBrief         = "ApproveBrief";
    public const string RequestChanges       = "RequestChanges";
    public const string ApproveRelease       = "ApproveRelease";
    public const string PullRequestReady     = "PullRequestReady";
    public const string ChecksCompleted      = "ChecksCompleted";
    public const string HumanReviewApproved  = "HumanReviewApproved";
    public const string HumanReviewRejected  = "HumanReviewRejected";
    public const string DeploymentCompleted  = "DeploymentCompleted";
    public const string RetryStage           = "RetryStage";
}
