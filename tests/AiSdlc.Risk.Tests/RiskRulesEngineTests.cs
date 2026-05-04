using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Risk.Tests;

public sealed class RiskRulesEngineTests
{
    private readonly IRiskRulesEngine _engine = new RiskRulesEngine();

    [Fact]
    public void Assess_ShouldReturnLowRiskForDocumentationOnlyChanges()
    {
        var request = new RiskAssessmentRequest
        {
            ChangedFilePaths = new[] { "docs/architecture.md", "README.md" }
        };

        var result = _engine.Assess(request);

        Assert.Equal(RiskLevel.Low, result.RiskLevel);
        Assert.Equal(RiskDecision.ContinueAutonomously, result.Decision);
        Assert.Contains(result.TriggeredSignals, signal => signal.Code == "docs-only-change");
        Assert.Single(result.TriggeredRules);
    }

    [Fact]
    public void Assess_ShouldReturnMediumRiskForTerraformChanges()
    {
        var request = new RiskAssessmentRequest
        {
            ChangedFilePaths = new[] { "infra/terraform/main.tf" },
            TerraformChanged = true
        };

        var result = _engine.Assess(request);

        Assert.Equal(RiskLevel.Medium, result.RiskLevel);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, signal => signal.Code == "terraform-change");
        Assert.Contains("Terraform infrastructure changed.", result.Rationale);
    }

    [Fact]
    public void Assess_ShouldReturnHighRiskForAuthenticationChanges()
    {
        var request = new RiskAssessmentRequest
        {
            ChangedFilePaths = new[] { "src/Auth/LoginController.cs" },
            AuthenticationChanged = true
        };

        var result = _engine.Assess(request);

        Assert.Equal(RiskLevel.High, result.RiskLevel);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, signal => signal.Code == "authentication-change");
    }

    [Fact]
    public void Assess_ShouldStopWorkflowWhenMandatoryQualityGateFails()
    {
        var request = new RiskAssessmentRequest
        {
            ChangedFilePaths = new[] { "src/AiSdlc.Shared/WorkflowRun.cs" },
            QualityGateResults = new[]
            {
                new QualityGateResult
                {
                    Name = "unit-tests",
                    Passed = false,
                    IsMandatory = true
                }
            }
        };

        var result = _engine.Assess(request);

        Assert.Equal(RiskLevel.High, result.RiskLevel);
        Assert.Equal(RiskDecision.StopWorkflow, result.Decision);
        Assert.Contains(result.TriggeredSignals, signal => signal.Code == "mandatory-quality-gate-failed");
    }

    [Fact]
    public void Assess_ShouldRequireHumanReviewWhenChangeScopeIsUnknown()
    {
        var request = new RiskAssessmentRequest();

        var result = _engine.Assess(request);

        Assert.Equal(RiskLevel.Unknown, result.RiskLevel);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, signal => signal.Code == "unknown-change-scope");
    }
}
