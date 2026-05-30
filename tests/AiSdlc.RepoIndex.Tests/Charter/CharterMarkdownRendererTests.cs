using AiSdlc.RepoIndex.Charter;
using Xunit;

namespace AiSdlc.RepoIndex.Tests.Charter;

public sealed class CharterMarkdownRendererTests
{
    [Fact]
    public void Renders_canonical_charter_shape()
    {
        var charter = new AiSdlc.RepoIndex.Charter.Charter
        {
            SchemaVersion = 1,
            Identity = new() { AppName = "TaskFlow", OneLineDescription = "Personal task tracker for indie devs" },
            Audience = new() { PrimaryUserDescription = "Solo developers", ExpectedScale = ExpectedScale.Solo },
            Purpose = new() { ProblemBeingSolved = "I lose track of tasks", SuccessCriteria = new[] { "Capture in <5s" } },
            Features = new[]
            {
                new CharterFeature
                {
                    Id = "f1", Name = "Capture task", Description = "Quick-add modal",
                    Status = FeatureStatus.Planned, Priority = FeaturePriority.MustHave, AddedIn = "charter"
                }
            },
            Constraints = new() { DataSensitivity = DataSensitivity.Low, NeedsAuth = true, NeedsPayments = false },
            AdditionalContext = "Mobile-first; offline important."
        };

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
    public void Omits_unknown_enum_values()
    {
        var charter = new AiSdlc.RepoIndex.Charter.Charter
        {
            SchemaVersion = 1,
            Identity = new() { AppName = "X" }
            // Audience.ExpectedScale, Constraints.DataSensitivity left at Unknown
        };

        var md = CharterMarkdownRenderer.Render(charter);

        Assert.DoesNotContain("Expected scale:", md);
        Assert.DoesNotContain("Data sensitivity:", md);
    }

    [Fact]
    public void Empty_features_section_is_omitted()
    {
        var charter = new AiSdlc.RepoIndex.Charter.Charter { SchemaVersion = 1, Identity = new() { AppName = "X" } };

        var md = CharterMarkdownRenderer.Render(charter);

        Assert.DoesNotContain("### Features", md);
    }
}
