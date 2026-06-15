using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class BootstrapRiskOverrideTests
{
    [Theory]
    [InlineData("HUMAN_REVIEW_REQUIRED")]
    [InlineData("AUTO_MERGE_ELIGIBLE")]
    [InlineData("BLOCKED")]
    [InlineData("anything-else")]
    public void Standard_mode_never_overrides(string decision)
    {
        var result = AiSdlcWorkflowOrchestrator.ApplyBootstrapRiskOverride(decision, WorkflowMode.Standard);
        Assert.Equal(decision, result);
    }

    [Fact]
    public void Bootstrap_promotes_HUMAN_REVIEW_REQUIRED_to_AUTO_MERGE_ELIGIBLE()
    {
        var result = AiSdlcWorkflowOrchestrator.ApplyBootstrapRiskOverride(
            "HUMAN_REVIEW_REQUIRED", WorkflowMode.Bootstrap);
        Assert.Equal("AUTO_MERGE_ELIGIBLE", result);
    }

    [Fact]
    public void Bootstrap_preserves_AUTO_MERGE_ELIGIBLE()
    {
        var result = AiSdlcWorkflowOrchestrator.ApplyBootstrapRiskOverride(
            "AUTO_MERGE_ELIGIBLE", WorkflowMode.Bootstrap);
        Assert.Equal("AUTO_MERGE_ELIGIBLE", result);
    }

    [Fact]
    public void Bootstrap_promotes_BLOCKED_to_AUTO_MERGE_ELIGIBLE()
    {
        // BLOCKED is no longer fatal in Bootstrap: the AI risk assessment non-deterministically
        // BLOCKed greenfield MVPs on aspirational compliance (no privacy policy / GDPR framework),
        // now addressed by the templated legal docs injected into every build. CI + the
        // verification gate remain the real gates; an unattended build must not die at risk.
        var result = AiSdlcWorkflowOrchestrator.ApplyBootstrapRiskOverride(
            "BLOCKED", WorkflowMode.Bootstrap);
        Assert.Equal("AUTO_MERGE_ELIGIBLE", result);
    }

    [Fact]
    public void Bootstrap_promotes_unknown_decisions_to_AUTO_MERGE_ELIGIBLE()
    {
        // Defensive: any decision auto-merges in Bootstrap. If the Risk Assessor invents a new
        // decision name, Bootstrap still trusts greenfield.
        var result = AiSdlcWorkflowOrchestrator.ApplyBootstrapRiskOverride(
            "SOMETHING_NEW", WorkflowMode.Bootstrap);
        Assert.Equal("AUTO_MERGE_ELIGIBLE", result);
    }
}
