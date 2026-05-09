using AiSdlc.Risk;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Risk.Tests;

public sealed class RiskRulesEngineTests
{
    private readonly IRiskRulesEngine _engine = new RiskRulesEngine();

    // ── Low risk ─────────────────────────────────────────────────────────────

    [Fact]
    public void Assess_DocsOnlyChange_ReturnsLowRiskAndContinueAutonomously()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            ChangedFilePaths = ["docs/architecture.md", "README.md"]
        });

        Assert.Equal(RiskLevel.Low, result.Level);
        Assert.Equal(RiskDecision.ContinueAutonomously, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "LOW_DOCS_ONLY");
    }

    [Fact]
    public void Assess_TestsOnlyChange_ReturnsLowRiskAndContinueAutonomously()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            ChangedFilePaths = ["tests/AiSdlc.Agents.Tests/BusinessAnalystAgentTests.cs"]
        });

        Assert.Equal(RiskLevel.Low, result.Level);
        Assert.Equal(RiskDecision.ContinueAutonomously, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "LOW_TESTS_ONLY");
    }

    [Fact]
    public void Assess_FrontendOnlyChange_ReturnsLowRiskAndContinueAutonomously()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            ChangedFilePaths =
            [
                "src/frontend/components/ProductCard.tsx",
                "src/frontend/pages/Home.tsx",
                "src/frontend/components/ProductCard.css"
            ]
        });

        Assert.Equal(RiskLevel.Low, result.Level);
        Assert.Equal(RiskDecision.ContinueAutonomously, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "LOW_FRONTEND_CONTENT");
    }

    // ── Medium risk ───────────────────────────────────────────────────────────

    [Fact]
    public void Assess_DatabaseMigrationFlag_ReturnsMediumRiskAndRequireHumanReview()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            DatabaseMigrationsChanged = true,
            ChangedFilePaths = ["src/api/Migrations/20240101_AddDeliveryTable.cs"]
        });

        Assert.Equal(RiskLevel.Medium, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "MEDIUM_DB_MIGRATION");
    }

    [Fact]
    public void Assess_TerraformFlag_ReturnsMediumRiskAndRequireHumanReview()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            TerraformChanged = true,
            ChangedFilePaths = ["infra/terraform/modules/function-app/main.tf"]
        });

        Assert.Equal(RiskLevel.Medium, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "MEDIUM_TERRAFORM");
    }

    [Fact]
    public void Assess_GitHubActionsWorkflowChanged_ReturnsMediumRisk()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            GitHubActionsWorkflowsChanged = true,
            ChangedFilePaths = [".github/workflows/ci.yml"]
        });

        Assert.Equal(RiskLevel.Medium, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "MEDIUM_GITHUB_ACTIONS");
    }

    // ── High risk ─────────────────────────────────────────────────────────────

    [Fact]
    public void Assess_AuthFlag_ReturnsHighRiskAndRequireHumanReview()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            AuthOrAuthorisationChanged = true,
            ChangedFilePaths = ["src/api/Auth/JwtTokenService.cs"]
        });

        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "HIGH_AUTH");
    }

    [Fact]
    public void Assess_PaymentFlag_ReturnsHighRiskAndRequireHumanReview()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            PaymentOrCheckoutChanged = true,
            ChangedFilePaths = ["src/api/Payment/StripeWebhookController.cs"]
        });

        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "HIGH_PAYMENT");
    }

    [Fact]
    public void Assess_PersonalDataFlag_ReturnsHighRisk()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            PersonalDataHandlingChanged = true
        });

        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "HIGH_PII");
    }

    [Fact]
    public void Assess_SecretsFlag_ReturnsHighRisk()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            SecretsOrKeyVaultChanged = true,
            ChangedFilePaths = ["infra/terraform/modules/key-vault/main.tf"]
        });

        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "HIGH_SECRETS");
    }

    [Fact]
    public void Assess_HighRiskOverridesMedium_WhenBothPresent()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            TerraformChanged = true,
            AuthOrAuthorisationChanged = true,
            ChangedFilePaths = ["infra/terraform/main.tf", "src/api/Auth/Startup.cs"]
        });

        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal(RiskDecision.RequireHumanReview, result.Decision);
    }

    // ── Blocked ───────────────────────────────────────────────────────────────

    [Fact]
    public void Assess_QualityGatesFailed_ReturnsStopWorkflow()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            QualityGatesPassed = false,
            ChangedFilePaths = ["docs/README.md"]
        });

        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal(RiskDecision.StopWorkflow, result.Decision);
        Assert.Contains(result.TriggeredSignals, s => s.Code == "BLOCKED_QUALITY_GATES");
    }

    // ── Unknown / ambiguous ───────────────────────────────────────────────────

    [Fact]
    public void Assess_NoMatchingSignals_ReturnsUnknownAndStopWorkflow()
    {
        var result = _engine.Assess(new RiskAssessmentRequest
        {
            ChangedFilePaths = []
        });

        Assert.Equal(RiskLevel.Unknown, result.Level);
        Assert.Equal(RiskDecision.StopWorkflow, result.Decision);
    }
}
