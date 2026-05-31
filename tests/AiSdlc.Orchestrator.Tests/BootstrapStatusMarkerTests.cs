using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class BootstrapStatusMarkerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Standard_mode_returns_no_marker(bool completed)
    {
        Assert.Null(AiSdlcWorkflowOrchestrator.GetTerminalStatusMarker(WorkflowMode.Standard, completed));
    }

    [Fact]
    public void Bootstrap_completed_returns_completed_marker()
    {
        var marker = AiSdlcWorkflowOrchestrator.GetTerminalStatusMarker(WorkflowMode.Bootstrap, completed: true);
        Assert.Equal("<!-- ai-sdlc:status=completed -->", marker);
    }

    [Fact]
    public void Bootstrap_failed_returns_failed_marker()
    {
        var marker = AiSdlcWorkflowOrchestrator.GetTerminalStatusMarker(WorkflowMode.Bootstrap, completed: false);
        Assert.Equal("<!-- ai-sdlc:status=failed -->", marker);
    }

    [Fact]
    public void Markers_are_html_comments_so_invisible_in_rendered_github()
    {
        // GitHub strips HTML comments from rendered comment bodies. Both markers must be
        // valid HTML comments (start "<!--", end "-->") so end users see nothing.
        var completed = AiSdlcWorkflowOrchestrator.GetTerminalStatusMarker(WorkflowMode.Bootstrap, true)!;
        var failed    = AiSdlcWorkflowOrchestrator.GetTerminalStatusMarker(WorkflowMode.Bootstrap, false)!;
        Assert.StartsWith("<!--", completed);
        Assert.EndsWith("-->", completed);
        Assert.StartsWith("<!--", failed);
        Assert.EndsWith("-->", failed);
    }
}
