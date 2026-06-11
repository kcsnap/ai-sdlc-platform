using System.Text.Json;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Orchestrator.Webhooks;
using AiSdlc.Shared;
using Microsoft.DurableTask.Client;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class ReconciliationSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 06, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Stranded_bootstrap_issue_qualifies()
    {
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel };
        Assert.True(ReconciliationSweepFunction.ShouldReconcile(labels, Now.AddHours(-1), Now));
    }

    [Fact]
    public void Fresh_issue_is_skipped_while_webhook_delivery_may_be_in_flight()
    {
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel };
        Assert.False(ReconciliationSweepFunction.ShouldReconcile(labels, Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Issue_with_progression_labels_is_skipped()
    {
        // Progression labels prove a run already handled this issue, even when its
        // orchestration instance was later purged by a close/reopen.
        var labels = new[]
        {
            GitHubWebhookProcessor.BootstrapLabel,
            "ai-sdlc:brief-approved",
            "ai-sdlc:analysis-ready"
        };
        Assert.False(ReconciliationSweepFunction.ShouldReconcile(labels, Now.AddHours(-1), Now));
    }

    [Fact]
    public void Non_sdlc_labels_do_not_disqualify()
    {
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel, "enhancement", "yorrixx" };
        Assert.True(ReconciliationSweepFunction.ShouldReconcile(labels, Now.AddHours(-1), Now));
    }

    [Fact]
    public void Label_matching_is_case_insensitive()
    {
        var labels = new[] { "AI-SDLC:Bootstrap", "AI-SDLC:Merged" };
        Assert.False(ReconciliationSweepFunction.ShouldReconcile(labels, Now.AddHours(-1), Now));
    }

    [Fact]
    public void Issue_with_exhausted_label_is_skipped_by_fresh_start_path()
    {
        var labels = new[]
        {
            GitHubWebhookProcessor.BootstrapLabel,
            ReconciliationSweepFunction.ExhaustedLabel
        };
        Assert.False(ReconciliationSweepFunction.ShouldReconcile(labels, Now.AddHours(-1), Now));
    }

    [Fact]
    public void Failed_instance_past_grace_qualifies_for_restart()
    {
        Assert.True(ReconciliationSweepFunction.ShouldRestartSilentFailure(
            OrchestrationRuntimeStatus.Failed, Now.AddHours(-1), Now));
    }

    [Fact]
    public void Recently_failed_instance_is_left_alone()
    {
        // An operator may be mid-way through a manual re-fire.
        Assert.False(ReconciliationSweepFunction.ShouldRestartSilentFailure(
            OrchestrationRuntimeStatus.Failed, Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Stale_failed_instance_is_left_alone()
    {
        // Restarting days-old failures burns model spend on abandoned builds; leaving the
        // Failed instance in place also blocks the fresh-start path, so it stays parked.
        Assert.False(ReconciliationSweepFunction.ShouldRestartSilentFailure(
            OrchestrationRuntimeStatus.Failed, Now.AddDays(-2), Now));
    }

    [Fact]
    public void Comment_over_github_limit_is_truncated_with_notice()
    {
        var oversized = new string('x', AgentActivityFunctions.GitHubCommentMaxChars + 5000);
        var truncated = AgentActivityFunctions.TruncateForGitHub(oversized);

        Assert.True(truncated.Length <= AgentActivityFunctions.GitHubCommentMaxChars);
        Assert.Contains("truncated", truncated);
    }

    [Fact]
    public void Comment_within_github_limit_is_unchanged()
    {
        var markdown = "## AI SDLC — Implementation\n\nAll good.";
        Assert.Same(markdown, AgentActivityFunctions.TruncateForGitHub(markdown));
    }

    [Theory]
    [InlineData(OrchestrationRuntimeStatus.Running)]
    [InlineData(OrchestrationRuntimeStatus.Completed)]
    [InlineData(OrchestrationRuntimeStatus.Pending)]
    [InlineData(OrchestrationRuntimeStatus.Terminated)]
    [InlineData(OrchestrationRuntimeStatus.Suspended)]
    public void Non_failed_statuses_are_never_restarted(OrchestrationRuntimeStatus status)
    {
        // Running may be waiting on human approval; Terminated/Suspended are operator actions;
        // Completed covers graceful business failures that already posted a terminal marker.
        Assert.False(ReconciliationSweepFunction.ShouldRestartSilentFailure(status, Now.AddHours(-1), Now));
    }

    [Fact]
    public void Restart_count_defaults_to_zero()
    {
        Assert.Equal(0, ReconciliationSweepFunction.ReadRestartCount(null));
        Assert.Equal(0, ReconciliationSweepFunction.ReadRestartCount(MakeContext()));
    }

    [Fact]
    public void Restart_count_survives_durable_json_round_trip()
    {
        var context = MakeContext();
        context.Metadata[ReconciliationSweepFunction.RestartCountMetadataKey] = "2";

        // Durable serialization round-trips Metadata values as JsonElement, not string.
        var roundTripped = JsonSerializer.Deserialize<AgentContext>(JsonSerializer.Serialize(context));

        Assert.Equal(2, ReconciliationSweepFunction.ReadRestartCount(roundTripped));
    }

    [Fact]
    public void Garbage_restart_count_is_treated_as_zero()
    {
        var context = MakeContext();
        context.Metadata[ReconciliationSweepFunction.RestartCountMetadataKey] = "not-a-number";
        Assert.Equal(0, ReconciliationSweepFunction.ReadRestartCount(context));
    }

    [Fact]
    public void Exhausted_comment_explains_recovery_path()
    {
        var comment = ReconciliationSweepFunction.BuildRestartsExhaustedComment(2);
        Assert.Contains("restarted 2 time(s)", comment);
        Assert.Contains(ReconciliationSweepFunction.ExhaustedLabel, comment);
        Assert.Contains("close/reopen", comment);
    }

    private static AgentContext MakeContext() => new()
    {
        RunId          = "yorrixx-apps_user-app-test_1",
        Repository     = "yorrixx-apps/user-app-test",
        IssueNumber    = 1,
        CurrentState   = "Started",
        RequestedAgent = "ProductStrategist"
    };

    [Theory]
    [InlineData(0, 0, 0, false)]  // no CI in the repo — nothing to gate on
    [InlineData(3, 0, 0, false)]  // all checks green
    [InlineData(3, 0, 1, true)]   // a check failed
    [InlineData(3, 2, 0, true)]   // checks never settled within the budget
    public void Checks_gate_blocks_on_failure_or_unsettled(int total, int pending, int failed, bool expectBlock)
    {
        var state = new ChecksState(total, pending,
            Enumerable.Range(0, failed).Select(i => $"check-{i}").ToList());
        Assert.Equal(expectBlock, AiSdlcWorkflowOrchestrator.ShouldBlockOnChecks(state));
    }

    [Fact]
    public void Checks_failed_comment_names_the_failing_checks()
    {
        var comment = AiSdlcWorkflowOrchestrator.BuildChecksFailedComment(
            new ChecksState(2, 0, ["build-test"]), "https://github.com/o/r/pull/5");
        Assert.Contains("build-test", comment);
        Assert.Contains("NOT merged", comment);
    }

    [Fact]
    public void Reopen_findings_take_comments_after_the_last_terminal_marker_only()
    {
        var comments = new[]
        {
            MakeComment("## AI SDLC — Implementation Review\n\nAPPROVED"),
            MakeComment("<!-- ai-sdlc:status=completed -->"),
            MakeComment("## Verification findings\n\nfrontend fails to compile: TS2304 in App.tsx"),
            MakeComment("Also the API returns 500 on /api/items."),
        };

        var findings = AgentActivityFunctions.ExtractReopenFindings(comments);

        Assert.Contains("TS2304", findings);
        Assert.Contains("500 on /api/items", findings);
        Assert.DoesNotContain("APPROVED", findings);
    }

    [Fact]
    public void Reopen_findings_exclude_platform_stage_comments_and_markers()
    {
        var comments = new[]
        {
            MakeComment("<!-- ai-sdlc:status=failed -->"),
            MakeComment("## AI SDLC — Refined Brief\n\n(new run already started)"),
            MakeComment("<!-- ai-sdlc:retry -->"),
        };

        Assert.Equal(string.Empty, AgentActivityFunctions.ExtractReopenFindings(comments));
    }

    [Fact]
    public void Reopen_findings_empty_when_no_comments()
    {
        Assert.Equal(string.Empty, AgentActivityFunctions.ExtractReopenFindings([]));
    }

    private static AiSdlc.GitHub.IssueComment MakeComment(string body) => new()
    {
        CommentId = 1, Repository = "o/r", IssueOrPullRequestNumber = 1,
        BodyMarkdown = body, AuthorLogin = "kcsnap", Url = "https://example"
    };

    [Fact]
    public void Implementation_summary_comment_carries_branch_metadata_and_no_code()
    {
        var comment = AiSdlcWorkflowOrchestrator.BuildImplementationSummaryComment(
            "AI SDLC — Implementation",
            "Code implementation generated for issue #1 (44 files from a 44-file manifest).",
            "yorrixx-apps/user-app-test", "main", "ai/1-build-app",
            "abc123def456789", 44);

        Assert.Contains("ai/1-build-app", comment);
        Assert.Contains("abc123def456", comment);          // short SHA
        Assert.Contains("Files changed:** 44", comment);
        Assert.Contains("compare/main...ai/1-build-app", comment);
        Assert.DoesNotContain("<file ", comment);          // never a code transport
        Assert.True(comment.Length < 2000);
    }

    [Fact]
    public void Stage_stalled_comment_explains_retry_command()
    {
        var comment = AiSdlcWorkflowOrchestrator.BuildStageStalledComment(
            "QA + Senior Coder", "Anthropic API returned 400: credit balance too low");

        Assert.Contains("/retry", comment);
        Assert.Contains("QA + Senior Coder", comment);
        Assert.Contains("credit balance too low", comment);
    }
}
