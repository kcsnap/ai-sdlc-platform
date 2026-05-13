namespace AiSdlc.Shared.AutoMerge;

/// <summary>
/// Evaluates all gates for autonomous merge eligibility.
/// All gates must pass; any failure blocks auto-merge.
/// </summary>
public sealed class AutoMergeEligibilityService : IAutoMergeEligibilityService
{
    public AutoMergeEligibilityResult Evaluate(AutoMergeEligibilityRequest request)
    {
        var failed = new List<string>();
        var passed = new List<string>();

        Check(request.RiskLevel == RiskLevel.Low,
            "Risk level is LOW",
            $"Risk level is {request.RiskLevel} (must be LOW for auto-merge)",
            passed, failed);

        Check(request.RiskDecision == "AUTO_MERGE_ELIGIBLE",
            "Risk decision is AUTO_MERGE_ELIGIBLE",
            $"Risk decision is '{request.RiskDecision}' (must be AUTO_MERGE_ELIGIBLE)",
            passed, failed);

        Check(request.BriefApproved,
            "Product brief approved by human",
            "Product brief has not been approved",
            passed, failed);

        Check(request.AllReviewsCompleted,
            "All agent reviews completed",
            "One or more agent reviews are incomplete",
            passed, failed);

        Check(request.NoBlockingIssues,
            "No blocking issues raised by any reviewer",
            "One or more blocking issues require resolution",
            passed, failed);

        Check(request.AllChecksPass,
            "All CI checks pass",
            "One or more CI checks are failing or incomplete",
            passed, failed);

        Check(request.HasTestCoverage,
            "Test coverage requirement met",
            "Test coverage does not meet requirements",
            passed, failed);

        Check(request.RollbackDocumented,
            "Rollback plan documented",
            "Rollback plan has not been documented",
            passed, failed);

        Check(request.ReleaseNotesGenerated,
            "Release notes generated",
            "Release notes have not been generated",
            passed, failed);

        Check(request.PostDeploymentChecksDefined,
            "Post-deployment checks defined",
            "Post-deployment checks have not been defined",
            passed, failed);

        var eligible = failed.Count == 0;
        var reason   = eligible
            ? $"All {passed.Count} gates passed — eligible for automatic merge and deployment."
            : $"{failed.Count} gate(s) failed — human review or remediation required before auto-merge.";

        return new AutoMergeEligibilityResult
        {
            IsEligible  = eligible,
            Reason      = reason,
            PassedGates = passed,
            FailedGates = failed
        };
    }

    private static void Check(bool condition, string passMessage, string failMessage,
        List<string> passed, List<string> failed)
    {
        if (condition) passed.Add(passMessage);
        else           failed.Add(failMessage);
    }
}
