namespace AiSdlc.Shared.AutoMerge;

/// <summary>
/// Determines whether a workflow run is eligible for automatic merge and deployment.
/// </summary>
public interface IAutoMergeEligibilityService
{
    AutoMergeEligibilityResult Evaluate(AutoMergeEligibilityRequest request);
}

public sealed record AutoMergeEligibilityRequest
{
    public required string RunId             { get; init; }
    public required string Repository        { get; init; }

    // Risk gate
    public required RiskLevel     RiskLevel          { get; init; }
    public required string        RiskDecision       { get; init; }

    // Agent gates
    public required bool BriefApproved               { get; init; }
    public required bool AllReviewsCompleted          { get; init; }
    public required bool NoBlockingIssues             { get; init; }

    // CI gates
    public required bool AllChecksPass               { get; init; }
    public required bool HasTestCoverage             { get; init; }

    // Compliance gates
    public required bool RollbackDocumented          { get; init; }
    public required bool ReleaseNotesGenerated       { get; init; }
    public required bool PostDeploymentChecksDefined  { get; init; }
}

public sealed record AutoMergeEligibilityResult
{
    public required bool   IsEligible    { get; init; }
    public required string Reason        { get; init; }
    public IReadOnlyList<string> FailedGates  { get; init; } = [];
    public IReadOnlyList<string> PassedGates  { get; init; } = [];
}
