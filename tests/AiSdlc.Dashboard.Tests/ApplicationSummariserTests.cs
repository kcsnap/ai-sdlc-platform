using AiSdlc.Dashboard.Services;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Dashboard.Tests;

public sealed class ApplicationSummariserTests
{
    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Empty(ApplicationSummariser.Summarise(Array.Empty<DashboardEvent>()));
    }

    [Fact]
    public void OneRepoOneRun_ProducesSingleApplicationWithCorrectCounts()
    {
        var events = new[]
        {
            MakeAgent("kcsnap_launchcart_28", "kcsnap/launchcart", 28, 0,  "Product Strategist", "Started",   "go"),
            MakeAgent("kcsnap_launchcart_28", "kcsnap/launchcart", 28, 1,  "Product Strategist", "Completed", "ok"),
            MakeAgent("kcsnap_launchcart_28", "kcsnap/launchcart", 28, 2,  "Release Manager",    "Completed", "PR opened")
        };

        var apps = ApplicationSummariser.Summarise(events);

        Assert.Single(apps);
        var app = apps[0];
        Assert.Equal("kcsnap/launchcart", app.Repository);
        Assert.Equal(1, app.TotalRuns);
        Assert.Equal(1, app.ReleasedCount);
        Assert.Equal(0, app.RunningCount);
        Assert.Equal(0, app.FailedCount);
        Assert.Equal("https://github.com/kcsnap/launchcart", app.RepositoryUrl);
    }

    [Fact]
    public void MultipleReposGroupedAndSortedByLatestActivity()
    {
        var events = new[]
        {
            // App A — older
            MakeAgent("orgA_repoA_1", "orgA/repoA", 1, 0, "Product Strategist", "Completed", "old"),
            // App B — newer
            MakeAgent("orgB_repoB_5", "orgB/repoB", 5, 100, "Product Strategist", "Started", "new")
        };

        var apps = ApplicationSummariser.Summarise(events);

        Assert.Equal(2, apps.Count);
        // Most-recently-active first
        Assert.Equal("orgB/repoB", apps[0].Repository);
        Assert.Equal("orgA/repoA", apps[1].Repository);
    }

    [Fact]
    public void Health_ReflectsWorstStatusAcrossRuns()
    {
        // One released run + one failed run on the same repo → "Has failures" takes precedence
        var events = new[]
        {
            MakeAgent("org_repo_1", "org/repo", 1, 0, "Product Strategist", "Completed", "ok"),
            MakeAgent("org_repo_1", "org/repo", 1, 1, "Release Manager",    "Completed", "released"),
            MakeAgent("org_repo_2", "org/repo", 2, 2, "Senior Coder",       "Failed",    "boom")
        };

        var app = ApplicationSummariser.Summarise(events).Single();
        Assert.Equal(2, app.TotalRuns);
        Assert.Equal(1, app.ReleasedCount);
        Assert.Equal(1, app.FailedCount);
        Assert.Equal("failed", app.HealthSlug);
        Assert.Equal("Has failures", app.HealthLabel);
    }

    [Fact]
    public void Health_ReleasedAndRunning_PrefersRunning()
    {
        var events = new[]
        {
            MakeAgent("org_repo_1", "org/repo", 1, 0, "Release Manager",    "Completed", "released"),
            MakeAgent("org_repo_2", "org/repo", 2, 1, "Product Strategist", "Started",   "in flight")
        };

        var app = ApplicationSummariser.Summarise(events).Single();
        Assert.Equal("running", app.HealthSlug);
        Assert.Equal("Active",  app.HealthLabel);
    }

    [Fact]
    public void LatestRun_IsTheMostRecentlyActiveOne()
    {
        var events = new[]
        {
            MakeAgent("org_repo_1", "org/repo", 1, 0,   "Product Strategist", "Completed", "issue 1"),
            MakeAgent("org_repo_5", "org/repo", 5, 100, "Product Strategist", "Started",   "issue 5 newer")
        };

        var app = ApplicationSummariser.Summarise(events).Single();
        Assert.NotNull(app.LatestRun);
        Assert.Equal(5, app.LatestRun!.IssueNumber);
    }

    private static DashboardEvent MakeAgent(string runId, string repo, int issue, int offset, string agent, string action, string summary)
    {
        var baseTs = new DateTimeOffset(2026, 5, 17, 14, 0, 0, TimeSpan.Zero);
        return DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId        = runId,
            TimestampUtc = baseTs.AddSeconds(offset),
            Repository   = repo,
            IssueNumber  = issue,
            ActorType    = "Agent",
            ActorName    = agent,
            Action       = action,
            Summary      = summary
        });
    }
}
