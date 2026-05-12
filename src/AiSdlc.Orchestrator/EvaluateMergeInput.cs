using AiSdlc.Shared;

namespace AiSdlc.Orchestrator;

public sealed record EvaluateMergeInput(
    string RunId,
    string Repository,
    RiskLevel RiskLevel,
    string RiskDecision,
    bool BriefApproved,
    bool AllReviewsCompleted,
    bool NoBlockingIssues,
    bool AllChecksPass,
    bool HasTestCoverage,
    bool RollbackDocumented,
    bool ReleaseNotesGenerated,
    bool PostDeploymentChecksDefined);
