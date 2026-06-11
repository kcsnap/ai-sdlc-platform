using AiSdlc.GitHub;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class WorkflowCommandParserTests
{
    [Theory]
    [InlineData("/approve-brief",                WorkflowCommand.ApproveBrief)]
    [InlineData("/APPROVE-BRIEF",                WorkflowCommand.ApproveBrief)]
    [InlineData("/approve-brief Looks great!",   WorkflowCommand.ApproveBrief)]
    [InlineData("/request-changes",              WorkflowCommand.RequestChanges)]
    [InlineData("/request-changes Need more detail on auth flow.", WorkflowCommand.RequestChanges)]
    [InlineData("/approve-release",              WorkflowCommand.ApproveRelease)]
    [InlineData("/approve-merge",                WorkflowCommand.ApproveMerge)]
    [InlineData("/APPROVE-MERGE",                WorkflowCommand.ApproveMerge)]
    [InlineData("/approve-merge LGTM",           WorkflowCommand.ApproveMerge)]
    [InlineData("/retry",                        WorkflowCommand.Retry)]
    [InlineData("/RETRY",                        WorkflowCommand.Retry)]
    [InlineData("/retry credits topped up",      WorkflowCommand.Retry)]
    public void KnownCommands_AreDetectedCorrectly(string body, WorkflowCommand expected)
    {
        Assert.Equal(expected, WorkflowCommandParser.Parse(body));
    }

    [Theory]
    [InlineData("Looks good to me!")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/unknown-command")]
    [InlineData("Some text\n/approve-brief")]  // command must be on first non-empty line
    public void UnknownOrNonCommandBodies_ReturnNone(string body)
    {
        Assert.Equal(WorkflowCommand.None, WorkflowCommandParser.Parse(body));
    }

    [Fact]
    public void CommandOnFirstLineWithBlankLinesAbove_IsDetected()
    {
        var body = "\n\n/approve-brief\nSome explanation below.";
        Assert.Equal(WorkflowCommand.ApproveBrief, WorkflowCommandParser.Parse(body));
    }

    [Fact]
    public void CommandFollowedByExtraWhitespace_IsDetected()
    {
        Assert.Equal(WorkflowCommand.ApproveBrief, WorkflowCommandParser.Parse("/approve-brief   "));
    }

    [Fact]
    public void CommandWithNoSpaceAfterPrefix_ButPartOfLongerWord_IsNotMatched()
    {
        // /approve-briefing is not /approve-brief
        Assert.Equal(WorkflowCommand.None, WorkflowCommandParser.Parse("/approve-briefing"));
    }

    [Fact]
    public void RetryPrefixAsPartOfLongerWord_IsNotMatched()
    {
        // /retrying is not /retry
        Assert.Equal(WorkflowCommand.None, WorkflowCommandParser.Parse("/retrying"));
    }
}
