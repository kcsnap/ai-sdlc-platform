using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class BootstrapMergeGateBypassTests
{
    [Theory]
    // Standard mode: requires risk + allowAutoMerge + eligibility
    [InlineData("AUTO_MERGE_ELIGIBLE",   true,  true,  WorkflowMode.Standard,  true)]
    [InlineData("AUTO_MERGE_ELIGIBLE",   false, true,  WorkflowMode.Standard,  false)] // gate failed → wait for human
    [InlineData("HUMAN_REVIEW_REQUIRED", true,  true,  WorkflowMode.Standard,  false)]
    [InlineData("AUTO_MERGE_ELIGIBLE",   true,  false, WorkflowMode.Standard,  false)] // repo didn't opt-in

    // Bootstrap mode: eligibility no longer required, but risk + allowAutoMerge still are
    [InlineData("AUTO_MERGE_ELIGIBLE",   true,  true,  WorkflowMode.Bootstrap, true)]
    [InlineData("AUTO_MERGE_ELIGIBLE",   false, true,  WorkflowMode.Bootstrap, true)]  // THE FIX: gate failure bypassed
    [InlineData("HUMAN_REVIEW_REQUIRED", false, true,  WorkflowMode.Bootstrap, false)] // shouldn't happen (risk override) but defensive
    [InlineData("AUTO_MERGE_ELIGIBLE",   false, false, WorkflowMode.Bootstrap, false)] // allowAutoMerge=false would be inconsistent with Bootstrap (PR #44 makes Bootstrap imply true) — guarded anyway
    public void ShouldAutoMerge_covers_all_combinations(
        string riskDecision, bool eligibilityIsEligible, bool allowAutoMerge,
        WorkflowMode mode, bool expected)
    {
        Assert.Equal(expected, AiSdlcWorkflowOrchestrator.ShouldAutoMerge(
            riskDecision, eligibilityIsEligible, allowAutoMerge, mode));
    }
}
