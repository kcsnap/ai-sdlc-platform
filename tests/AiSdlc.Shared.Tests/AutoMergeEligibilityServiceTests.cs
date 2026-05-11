using AiSdlc.Shared.AutoMerge;
using Xunit;

namespace AiSdlc.Shared.Tests;

public sealed class AutoMergeEligibilityServiceTests
{
    private readonly AutoMergeEligibilityService _svc = new();

    private static AutoMergeEligibilityRequest AllPass => new()
    {
        RunId                        = "run-1",
        Repository                   = "owner/repo",
        RiskLevel                    = RiskLevel.Low,
        RiskDecision                 = "AUTO_MERGE_ELIGIBLE",
        BriefApproved                = true,
        AllReviewsCompleted          = true,
        NoBlockingIssues             = true,
        AllChecksPass                = true,
        HasTestCoverage              = true,
        RollbackDocumented           = true,
        ReleaseNotesGenerated        = true,
        PostDeploymentChecksDefinied = true
    };

    [Fact]
    public void Evaluate_AllGatesPass_IsEligible()
    {
        var result = _svc.Evaluate(AllPass);
        Assert.True(result.IsEligible);
        Assert.Empty(result.FailedGates);
        Assert.NotEmpty(result.PassedGates);
    }

    [Fact]
    public void Evaluate_HighRisk_NotEligible()
    {
        var req    = AllPass with { RiskLevel = RiskLevel.High, RiskDecision = "HUMAN_REVIEW_REQUIRED" };
        var result = _svc.Evaluate(req);
        Assert.False(result.IsEligible);
        Assert.Contains(result.FailedGates, f => f.Contains("Risk level"));
    }

    [Fact]
    public void Evaluate_BriefNotApproved_NotEligible()
    {
        var req    = AllPass with { BriefApproved = false };
        var result = _svc.Evaluate(req);
        Assert.False(result.IsEligible);
        Assert.Contains(result.FailedGates, f => f.Contains("brief"));
    }

    [Fact]
    public void Evaluate_BlockingIssues_NotEligible()
    {
        var req    = AllPass with { NoBlockingIssues = false };
        var result = _svc.Evaluate(req);
        Assert.False(result.IsEligible);
        Assert.Contains(result.FailedGates, f => f.Contains("blocking"));
    }

    [Fact]
    public void Evaluate_CiChecksFailing_NotEligible()
    {
        var req    = AllPass with { AllChecksPass = false };
        var result = _svc.Evaluate(req);
        Assert.False(result.IsEligible);
        Assert.Contains(result.FailedGates, f => f.Contains("CI"));
    }

    [Fact]
    public void Evaluate_RollbackNotDocumented_NotEligible()
    {
        var req    = AllPass with { RollbackDocumented = false };
        var result = _svc.Evaluate(req);
        Assert.False(result.IsEligible);
        Assert.Contains(result.FailedGates, f => f.Contains("Rollback"));
    }

    [Fact]
    public void Evaluate_MultipleFailures_ReportsAll()
    {
        var req    = AllPass with { RiskLevel = RiskLevel.High, RiskDecision = "HUMAN_REVIEW_REQUIRED", AllChecksPass = false };
        var result = _svc.Evaluate(req);
        Assert.False(result.IsEligible);
        Assert.True(result.FailedGates.Count >= 3);
    }
}
