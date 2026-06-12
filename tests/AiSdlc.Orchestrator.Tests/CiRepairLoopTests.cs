using AiSdlc.GitHub;
using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class CiRepairLoopTests
{
    [Theory]
    [InlineData(1, 0, 0, true)]   // concrete failure, attempts available
    [InlineData(1, 0, 2, false)]  // attempts exhausted
    [InlineData(0, 2, 0, false)]  // pure pending-timeout — no findings to act on
    [InlineData(1, 1, 0, true)]   // fast-fail build + slow e2e at budget — actionable
    [InlineData(0, 0, 0, false)]  // nothing failed
    public void Repair_attempted_only_on_concrete_failures_within_budget(int failed, int pending, int attemptsUsed, bool expect)
    {
        var state = new ChecksState(failed + pending, pending,
            Enumerable.Range(0, failed).Select(i => $"check-{i}").ToList());
        Assert.Equal(expect, AiSdlcWorkflowOrchestrator.ShouldAttemptCiRepair(state, attemptsUsed));
    }

    [Fact]
    public void Repair_attempt_comment_is_a_summary_with_no_code()
    {
        var comment = AiSdlcWorkflowOrchestrator.BuildCiRepairAttemptComment(
            1, 2, 3, ["build-api", "build-frontend"],
            "yorrixx-apps/user-app-x", "main", "ai/1-build", "abc123def456789");

        Assert.StartsWith("## AI SDLC", comment);
        Assert.Contains("attempt 1 of 2", comment);
        Assert.Contains("build-api", comment);
        Assert.Contains("3 file(s)", comment);
        Assert.Contains("abc123def456", comment);
        Assert.Contains("compare/main...ai/1-build", comment);
        Assert.DoesNotContain("<file", comment);
    }

    [Fact]
    public void Checks_failed_comment_mentions_repair_attempts_only_when_used()
    {
        var state = new ChecksState(2, 0, ["build-api"]);

        var without = AiSdlcWorkflowOrchestrator.BuildChecksFailedComment(state, "https://pr", 0);
        Assert.DoesNotContain("repair", without, StringComparison.OrdinalIgnoreCase);

        var with = AiSdlcWorkflowOrchestrator.BuildChecksFailedComment(state, "https://pr", 2);
        Assert.Contains("attempted 2 time(s)", with);
    }

    [Fact]
    public void Findings_render_annotations_as_path_line_level_message()
    {
        var findings = new List<FailedCheckFinding>
        {
            new("build-api",
                [new CheckAnnotation("src/api/Program.cs", 12, "failure", "CS0103: name 'Foo' does not exist")],
                LogTail: null)
        };

        var rendered = AgentActivityFunctions.RenderCiFindings(findings);

        Assert.Contains("## Check: build-api", rendered);
        Assert.Contains("src/api/Program.cs:12 [failure] CS0103", rendered);
    }

    [Fact]
    public void Findings_use_log_tail_when_no_annotations_and_skip_empty_checks()
    {
        var findings = new List<FailedCheckFinding>
        {
            new("build-frontend", [], "error TS2304: Cannot find name 'Foo'."),
            new("mystery-check", [], LogTail: null), // nothing extractable — skipped
        };

        var rendered = AgentActivityFunctions.RenderCiFindings(findings);

        Assert.Contains("TS2304", rendered);
        Assert.Contains("```", rendered);
        Assert.DoesNotContain("mystery-check", rendered);
    }

    [Fact]
    public void Findings_render_empty_when_nothing_actionable()
    {
        var findings = new List<FailedCheckFinding> { new("x", [], null) };
        Assert.Equal(string.Empty, AgentActivityFunctions.RenderCiFindings(findings));
    }

    [Fact]
    public void Findings_total_cap_drops_whole_check_sections_from_the_front()
    {
        var bigTail = new string('x', AgentActivityFunctions.CiFindingsPerCheckMaxChars);
        var findings = Enumerable.Range(0, 5)
            .Select(i => new FailedCheckFinding($"check-{i}", [], bigTail))
            .ToList();

        var rendered = AgentActivityFunctions.RenderCiFindings(findings);

        Assert.True(rendered.Length <= AgentActivityFunctions.CiFindingsMaxChars);
        Assert.Contains("check-4", rendered);       // newest sections survive
        Assert.DoesNotContain("## Check: check-0", rendered); // oldest dropped whole
    }
}
