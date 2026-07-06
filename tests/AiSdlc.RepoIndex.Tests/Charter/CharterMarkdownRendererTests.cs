using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;
using Xunit;

namespace AiSdlc.RepoIndex.Tests.Charter;

public sealed class CharterMarkdownRendererTests
{
    [Fact]
    public void Renders_canonical_charter_shape()
    {
        var charter = TestCharters.Make(
            appName: "TaskFlow",
            description: "Personal task tracker for indie devs",
            primaryUser: "Solo developers",
            scale: ExpectedScale.Solo,
            problem: "I lose track of tasks",
            successCriteria: ["Capture in <5s"],
            features:
            [
                new CharterFeature("f1", "Capture task", "Quick-add modal", FeatureStatus.Planned, "charter", FeaturePriority.MustHave)
            ],
            sensitivity: DataSensitivity.Low,
            auth: true,
            additionalContext: "Mobile-first; offline important.");

        var md = CharterMarkdownRenderer.Render(charter);

        Assert.Contains("## App Charter (v1)", md);
        Assert.Contains("**App:** TaskFlow", md);
        Assert.Contains("**Description:** Personal task tracker for indie devs", md);
        Assert.Contains("**Expected scale:** Solo", md);
        Assert.Contains("**Problem:** I lose track of tasks", md);
        Assert.Contains("Capture in <5s", md);
        Assert.Contains("**Capture task** (MustHave) [Planned] — Quick-add modal", md);
        Assert.Contains("**Data sensitivity:** Low", md);
        Assert.Contains("Needs auth: yes", md);
        Assert.Contains("Needs payments: no", md);
        Assert.Contains("Mobile-first; offline important.", md);
    }

    [Fact]
    public void Structured_integrations_render_name_and_purpose()
    {
        var charter = TestCharters.Make(integrations:
        [
            new CharterIntegration("Stripe", "payments"),
            new CharterIntegration("Postmark", "")
        ]);

        var md = CharterMarkdownRenderer.Render(charter);

        Assert.Contains("### Integrations", md);
        Assert.Contains("- **Stripe** — payments", md);
        Assert.Contains("- Postmark", md);
    }

    // The package enums carry no Unknown sentinel, so scale/sensitivity always render now
    // (pre-package behaviour omitted them when left at Unknown).
    [Fact]
    public void Scale_and_sensitivity_always_render()
    {
        var md = CharterMarkdownRenderer.Render(TestCharters.Make(appName: "X"));

        Assert.Contains("**Expected scale:** Solo", md);
        Assert.Contains("**Data sensitivity:** Low", md);
    }

    [Fact]
    public void Empty_features_section_is_omitted()
    {
        var md = CharterMarkdownRenderer.Render(TestCharters.Make(appName: "X"));

        Assert.DoesNotContain("### Features", md);
    }
}
