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
    public void Wedged_running_instance_past_threshold_qualifies_for_reclamation()
    {
        // v004 baseline: a heavy build hung on a rate-limited model call and sat in Running with a
        // frozen lastUpdatedTime, never failing — exactly what this rescues.
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel, "ai-sdlc:analysis-ready" };
        Assert.True(ReconciliationSweepFunction.ShouldRestartStuckRunning(
            OrchestrationRuntimeStatus.Running, Now.AddMinutes(-25), Now, labels));
    }

    [Fact]
    public void Merely_slow_running_instance_is_left_alone()
    {
        // A single rate-limited model call can legitimately run a few minutes — below the
        // wedge threshold it is slow, not stuck.
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel };
        Assert.False(ReconciliationSweepFunction.ShouldRestartStuckRunning(
            OrchestrationRuntimeStatus.Running, Now.AddMinutes(-10), Now, labels));
    }

    [Fact]
    public void Stale_running_instance_is_left_alone()
    {
        // Past MaxRestartableAge it is an abandoned build — restarting burns spend on no one,
        // same policy as the Failed path.
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel };
        Assert.False(ReconciliationSweepFunction.ShouldRestartStuckRunning(
            OrchestrationRuntimeStatus.Running, Now.AddHours(-7), Now, labels));
    }

    [Theory]
    [InlineData("ai-sdlc:awaiting-brief-approval")]
    [InlineData("ai-sdlc:awaiting-human-review")]
    [InlineData("AI-SDLC:Awaiting-Human-Review")] // gates are matched case-insensitively
    public void Running_instance_parked_at_a_human_gate_is_never_reclaimed(string awaitingLabel)
    {
        // These gates leave the orchestration in Running with a frozen lastUpdatedTime for as long
        // as the human takes — the awaiting-* label is what keeps the sweep off them.
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel, awaitingLabel };
        Assert.False(ReconciliationSweepFunction.ShouldRestartStuckRunning(
            OrchestrationRuntimeStatus.Running, Now.AddMinutes(-30), Now, labels));
    }

    [Theory]
    [InlineData(OrchestrationRuntimeStatus.Failed)]
    [InlineData(OrchestrationRuntimeStatus.Completed)]
    [InlineData(OrchestrationRuntimeStatus.Pending)]
    [InlineData(OrchestrationRuntimeStatus.Terminated)]
    [InlineData(OrchestrationRuntimeStatus.Suspended)]
    public void Non_running_statuses_are_never_treated_as_wedged(OrchestrationRuntimeStatus status)
    {
        // Failed has its own path; Terminated/Suspended are operator actions; the wedge rescue
        // only ever fires on a genuinely-Running instance.
        var labels = new[] { GitHubWebhookProcessor.BootstrapLabel };
        Assert.False(ReconciliationSweepFunction.ShouldRestartStuckRunning(
            status, Now.AddMinutes(-30), Now, labels));
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

    [Theory]
    [InlineData(0, 0, 0, true)]   // zero checks within the grace — Actions may not have registered yet
    [InlineData(0, 0, 3, true)]   // still within grace
    [InlineData(0, 0, 4, false)]  // grace exhausted — repo genuinely has no CI
    [InlineData(3, 2, 0, true)]   // checks running
    [InlineData(3, 0, 0, false)]  // settled
    public void Gate_keeps_polling_through_the_zero_check_registration_grace(int total, int pending, int poll, bool keepPolling)
    {
        var state = new ChecksState(total, pending, []);
        Assert.Equal(keepPolling, AiSdlcWorkflowOrchestrator.ShouldKeepPollingChecks(state, poll));
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
    public void Repair_source_selection_excludes_generated_binary_and_oversized_files()
    {
        var tree = new List<AiSdlc.GitHub.RepoTreeEntry>
        {
            new("src/frontend/src/App.tsx", 2000),
            new("src/frontend/package-lock.json", 30000),          // lockfile — excluded by name
            new("src/frontend/public/logo.png", 5000),             // binary — excluded by extension
            new("node_modules/react/index.js", 100),               // excluded by prefix
            new("src/api/Big.cs", AgentActivityFunctions.RepairSourceMaxFileBytes + 1), // oversized
            new("src/api/Program.cs", 3000),
        };

        var selected = AgentActivityFunctions.SelectRepairSourcePaths(tree);

        Assert.Equal(["src/frontend/src/App.tsx", "src/api/Program.cs"], selected);
    }

    [Fact]
    public void Repair_source_selection_respects_the_total_budget()
    {
        // 30KB files (under the per-file cap) against the total budget — derive the expected
        // count from the constant so the test survives budget tuning.
        const int fileSize = 30_000;
        var expectedFit = AgentActivityFunctions.RepairSourceTotalBudget / fileSize;
        var tree = Enumerable.Range(0, expectedFit + 5)
            .Select(i => new AiSdlc.GitHub.RepoTreeEntry($"src/f{i}.ts", fileSize))
            .ToList();

        Assert.Equal(expectedFit, AgentActivityFunctions.SelectRepairSourcePaths(tree).Count);
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
