using AiSdlc.Audit;
using AiSdlc.Dashboard.Services;
using AiSdlc.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiSdlc.Dashboard.Tests;

public sealed class AuditFeedServiceTests
{
    [Fact]
    public async Task Service_PublishesNewlyWrittenAuditEventsToBus()
    {
        var audit = new InMemoryAuditService();
        var bus   = new DashboardEventBus(capacity: 50);
        var seen  = new List<DashboardEvent>();
        using var _ = bus.Subscribe(batch => { seen.AddRange(batch); return Task.CompletedTask; });

        var options = Options.Create(new DashboardOptions
        {
            PollIntervalSeconds = 1,
            BackfillSize        = 50,
            MaxEventsInMemory   = 50
        });

        var service = new AuditFeedService(audit, bus, options, NullLogger<AuditFeedService>.Instance);

        using var cts = new CancellationTokenSource();
        var run = service.StartAsync(cts.Token);

        // Write an audit event after the service starts polling.
        await Task.Delay(200, CancellationToken.None);
        await audit.WriteAsync(new AuditEvent
        {
            RunId       = "run-1",
            Repository  = "org/repo",
            IssueNumber = 1,
            ActorType   = "Agent",
            ActorName   = "BusinessAnalyst",
            Action      = "Reviewed",
            Summary     = "Spec ok",
            TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(1)  // ensure > highWaterMark
        }, CancellationToken.None);

        // Allow at least one poll cycle to fire.
        for (var attempt = 0; attempt < 30 && seen.Count == 0; attempt++)
        {
            await Task.Delay(150, CancellationToken.None);
        }

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
        await run;

        Assert.NotEmpty(seen);
        Assert.Contains(seen, e => e.RunId == "run-1" && e.Action == "Reviewed");
    }

    [Fact]
    public void DashboardEvent_FromAuditEvent_DerivesApiAndArtefactFlag()
    {
        var agent = DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId = "r", Repository = "o/r", IssueNumber = 1,
            ActorType = "Agent", ActorName = "BusinessAnalyst",
            Action = "Completed", Summary = "ok"
        });

        Assert.Equal("agent:BusinessAnalyst", agent.Api);
        Assert.True(agent.HasPromptArtefact);
        Assert.False(agent.IsError);

        var webhook = DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId = "r", Repository = "o/r", IssueNumber = 1,
            ActorType = "Webhook", ActorName = "/github/webhook",
            Action = "issues.opened", Summary = "got it"
        });

        Assert.Equal("POST /github/webhook", webhook.Api);
        Assert.False(webhook.HasPromptArtefact);
        Assert.False(webhook.IsError);
    }

    [Fact]
    public void DashboardEvent_FromAuditEvent_DetectsFailedAgentAndExtractsErrorFields()
    {
        var failed = DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId       = "run-9",
            Repository  = "org/repo",
            IssueNumber = 42,
            ActorType   = "Agent",
            ActorName   = "BusinessAnalyst",
            Action      = "Failed",
            Summary     = "Anthropic API returned 429 Too Many Requests",
            References  = new Dictionary<string, string>
            {
                ["exceptionType"] = "System.Net.Http.HttpRequestException",
                ["stackTrace"]    = "at AiSdlc.ModelProviders.AnthropicModelProvider.GenerateAsync (line 87)..."
            }
        });

        Assert.True(failed.IsError);
        Assert.False(failed.HasPromptArtefact);  // no prompt blob for failed runs
        Assert.Equal("System.Net.Http.HttpRequestException", failed.ErrorType);
        Assert.Contains("AnthropicModelProvider", failed.StackTrace);
        Assert.Equal("Anthropic API returned 429 Too Many Requests", failed.Summary);
    }

    [Fact]
    public void DashboardEvent_FromAuditEvent_ExposesReferencesDictionary()
    {
        var ev = DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId       = "r",
            Repository  = "o/r",
            IssueNumber = 1,
            ActorType   = "Comment",
            ActorName   = "GitHubComment",
            Action      = "Posted",
            Summary     = "Comment posted",
            References  = new Dictionary<string, string>
            {
                ["commentUrl"] = "https://github.com/o/r/issues/1#issuecomment-99",
                ["commentId"]  = "99"
            }
        });

        Assert.Equal(2, ev.References.Count);
        Assert.Equal("99", ev.References["commentId"]);
        Assert.Equal("https://github.com/o/r/issues/1#issuecomment-99", ev.CommentUrl);
    }

    [Fact]
    public void DashboardEvent_FromAuditEvent_StartedAgent_HasNoPromptArtefact()
    {
        var started = DashboardEvent.FromAuditEvent(new AuditEvent
        {
            RunId = "r", Repository = "o/r", IssueNumber = 1,
            ActorType = "Agent", ActorName = "QaTestEngineer",
            Action = "Started", Summary = "QaTestEngineer started"
        });

        Assert.False(started.IsError);
        Assert.False(started.HasPromptArtefact);
    }
}
