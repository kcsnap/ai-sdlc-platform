using AiSdlc.GitHub.Webhooks;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class WorkflowModeResolutionTests
{
    [Fact]
    public void Returns_Standard_when_labels_empty()
    {
        var mode = GitHubWebhookFunction.ResolveWorkflowMode(Array.Empty<WebhookLabel>());
        Assert.Equal(WorkflowMode.Standard, mode);
    }

    [Fact]
    public void Returns_Standard_when_bootstrap_label_absent()
    {
        var labels = new[]
        {
            new WebhookLabel { Name = "bug" },
            new WebhookLabel { Name = "ai-sdlc:awaiting-brief-approval" }
        };

        var mode = GitHubWebhookFunction.ResolveWorkflowMode(labels);

        Assert.Equal(WorkflowMode.Standard, mode);
    }

    [Fact]
    public void Returns_Bootstrap_when_label_present_alone()
    {
        var labels = new[] { new WebhookLabel { Name = GitHubWebhookFunction.BootstrapLabel } };

        var mode = GitHubWebhookFunction.ResolveWorkflowMode(labels);

        Assert.Equal(WorkflowMode.Bootstrap, mode);
    }

    [Fact]
    public void Returns_Bootstrap_when_label_present_among_others()
    {
        var labels = new[]
        {
            new WebhookLabel { Name = "enhancement" },
            new WebhookLabel { Name = GitHubWebhookFunction.BootstrapLabel },
            new WebhookLabel { Name = "yorrixx" }
        };

        var mode = GitHubWebhookFunction.ResolveWorkflowMode(labels);

        Assert.Equal(WorkflowMode.Bootstrap, mode);
    }

    [Fact]
    public void Label_matching_is_case_insensitive()
    {
        var labels = new[] { new WebhookLabel { Name = "AI-SDLC:Bootstrap" } };

        var mode = GitHubWebhookFunction.ResolveWorkflowMode(labels);

        Assert.Equal(WorkflowMode.Bootstrap, mode);
    }
}
