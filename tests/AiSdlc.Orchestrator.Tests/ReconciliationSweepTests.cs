using AiSdlc.Orchestrator.Functions;
using AiSdlc.Orchestrator.Webhooks;
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
    public void Stage_stalled_comment_explains_retry_command()
    {
        var comment = AiSdlcWorkflowOrchestrator.BuildStageStalledComment(
            "QA + Senior Coder", "Anthropic API returned 400: credit balance too low");

        Assert.Contains("/retry", comment);
        Assert.Contains("QA + Senior Coder", comment);
        Assert.Contains("credit balance too low", comment);
    }
}
