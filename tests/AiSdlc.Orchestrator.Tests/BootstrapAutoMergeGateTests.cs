using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class BootstrapAutoMergeGateTests
{
    [Theory]
    [InlineData(false, WorkflowMode.Standard,  false)]
    [InlineData(true,  WorkflowMode.Standard,  true)]
    [InlineData(false, WorkflowMode.Bootstrap, true)]   // The fix: Bootstrap unblocks even when the repo flag is off
    [InlineData(true,  WorkflowMode.Bootstrap, true)]
    public void ShouldAllowAutoMerge_covers_all_combinations(bool repoFlag, WorkflowMode mode, bool expected)
    {
        Assert.Equal(expected, AiSdlcWorkflowOrchestrator.ShouldAllowAutoMerge(repoFlag, mode));
    }
}
