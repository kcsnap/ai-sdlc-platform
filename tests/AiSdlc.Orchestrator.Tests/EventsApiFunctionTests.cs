using AiSdlc.Audit;
using AiSdlc.Events.Contract;
using AiSdlc.Orchestrator.Events;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class EventsApiFunctionTests
{
    private const string RunId = "kcsnap_ai-sdlc-platform_123";
    private const string Repo = "kcsnap/ai-sdlc-platform";

    [Fact]
    public async Task BuildResponse_UnknownRunId_ReturnsEmptyPage()
    {
        var function = BuildFunction(new InMemoryAuditService());

        var response = await function.BuildResponseAsync("unknown_run", sinceCursor: null, limit: 100, CancellationToken.None);

        Assert.Empty(response.Events);
        Assert.False(response.HasMore);
        Assert.Equal(string.Empty, response.NextCursor);
    }

    [Fact]
    public async Task BuildResponse_FullStream_ReturnsAllEvents_NoMore()
    {
        var audit = new InMemoryAuditService();
        await audit.WriteAsync(MakeAgentStarted("Architect"), CancellationToken.None);
        await audit.WriteAsync(MakeAgentCompleted("Architect"), CancellationToken.None);

        var function = BuildFunction(audit);
        var response = await function.BuildResponseAsync(RunId, sinceCursor: null, limit: 100, CancellationToken.None);

        Assert.Equal(2, response.Events.Count);
        Assert.False(response.HasMore);
        Assert.NotEmpty(response.NextCursor);
    }

    [Fact]
    public async Task BuildResponse_HitsLimit_SetsHasMoreTrue_TruncatesAtLimit()
    {
        var audit = new InMemoryAuditService();
        for (var i = 0; i < 5; i++)
        {
            await audit.WriteAsync(MakeAgentStarted($"Agent{i}"), CancellationToken.None);
        }

        var function = BuildFunction(audit);
        var response = await function.BuildResponseAsync(RunId, sinceCursor: null, limit: 2, CancellationToken.None);

        Assert.Equal(2, response.Events.Count);
        Assert.True(response.HasMore);
    }

    [Fact]
    public async Task BuildResponse_WithCursor_AdvancesAndReturnsRest()
    {
        var audit = new InMemoryAuditService();
        for (var i = 0; i < 4; i++)
        {
            await audit.WriteAsync(MakeAgentStarted($"Agent{i}"), CancellationToken.None);
        }

        var function = BuildFunction(audit);

        var firstPage = await function.BuildResponseAsync(RunId, sinceCursor: null, limit: 2, CancellationToken.None);
        Assert.True(firstPage.HasMore);

        var secondPage = await function.BuildResponseAsync(RunId, firstPage.NextCursor, limit: 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Events.Count);
        Assert.False(secondPage.HasMore);

        // No overlap between pages.
        var firstCursors = firstPage.Events.Select(e => e.Cursor).ToHashSet();
        Assert.DoesNotContain(secondPage.Events[0].Cursor, firstCursors);
    }

    [Fact]
    public async Task BuildResponse_EmptyPageBehindCursor_ReturnsInputCursor_HasMoreFalse()
    {
        var audit = new InMemoryAuditService();
        await audit.WriteAsync(MakeAgentStarted("Architect"), CancellationToken.None);

        var function = BuildFunction(audit);
        var firstPage = await function.BuildResponseAsync(RunId, sinceCursor: null, limit: 100, CancellationToken.None);
        var tailCursor = firstPage.NextCursor;

        // Polling again from the tail should yield 0 events but return the same cursor.
        var nextPoll = await function.BuildResponseAsync(RunId, tailCursor, limit: 100, CancellationToken.None);

        Assert.Empty(nextPoll.Events);
        Assert.False(nextPoll.HasMore);
        Assert.Equal(tailCursor, nextPoll.NextCursor);
    }

    [Fact]
    public async Task BuildResponse_SkipsUnmappableEvents_ButAdvancesCursorPastThem()
    {
        var audit = new InMemoryAuditService();
        // Three events: known + unmappable (futuristic actor type) + known.
        await audit.WriteAsync(MakeAgentStarted("Architect"), CancellationToken.None);
        await audit.WriteAsync(new AuditEvent
        {
            RunId = RunId,
            TimestampUtc = DateTimeOffset.UtcNow,
            Repository = Repo,
            IssueNumber = 123,
            ActorType = "FuturisticActorType",
            ActorName = "x",
            Action = "y",
            Summary = "z",
        }, CancellationToken.None);
        await audit.WriteAsync(MakeAgentCompleted("Architect"), CancellationToken.None);

        var function = BuildFunction(audit);
        var response = await function.BuildResponseAsync(RunId, sinceCursor: null, limit: 100, CancellationToken.None);

        // Two events surface; the futuristic one is silently skipped.
        Assert.Equal(2, response.Events.Count);
        Assert.Equal(EventType.AgentStarted, response.Events[0].EventType);
        Assert.Equal(EventType.AgentCompleted, response.Events[1].EventType);
    }

    [Fact]
    public void ParseLimit_DefaultsTo100_WhenMissingOrInvalid()
    {
        Assert.Equal(EventsApiFunction.DefaultLimit, EventsApiFunction.ParseLimit(null));
        Assert.Equal(EventsApiFunction.DefaultLimit, EventsApiFunction.ParseLimit(string.Empty));
        Assert.Equal(EventsApiFunction.DefaultLimit, EventsApiFunction.ParseLimit("not-a-number"));
    }

    [Fact]
    public void ParseLimit_ClampsSilently()
    {
        Assert.Equal(1, EventsApiFunction.ParseLimit("0"));
        Assert.Equal(1, EventsApiFunction.ParseLimit("-5"));
        Assert.Equal(EventsApiFunction.MaxLimit, EventsApiFunction.ParseLimit("9999"));
        Assert.Equal(250, EventsApiFunction.ParseLimit("250"));
    }

    [Fact]
    public void ParseQueryString_HandlesMissingAndEmptyAndMultiplePairs()
    {
        Assert.Empty(EventsApiFunction.ParseQueryString(string.Empty));
        Assert.Empty(EventsApiFunction.ParseQueryString("?"));

        var parsed = EventsApiFunction.ParseQueryString("?since=abc&limit=50");
        Assert.Equal("abc", parsed["since"]);
        Assert.Equal("50", parsed["limit"]);

        var noQuestionMark = EventsApiFunction.ParseQueryString("since=xyz");
        Assert.Equal("xyz", noQuestionMark["since"]);
    }

    [Fact]
    public void ParseQueryString_UrlDecodes()
    {
        var parsed = EventsApiFunction.ParseQueryString("?since=a%2Bb%3D");
        Assert.Equal("a+b=", parsed["since"]);
    }

    private static EventsApiFunction BuildFunction(IAuditService audit) =>
        new(audit, NullLogger<EventsApiFunction>.Instance);

    private static AuditEvent MakeAgentStarted(string agentName) => new()
    {
        RunId = RunId,
        TimestampUtc = DateTimeOffset.UtcNow,
        Repository = Repo,
        IssueNumber = 123,
        ActorType = "Agent",
        ActorName = agentName,
        Action = "Started",
        Summary = $"{agentName} starting"
    };

    private static AuditEvent MakeAgentCompleted(string agentName) => new()
    {
        RunId = RunId,
        TimestampUtc = DateTimeOffset.UtcNow,
        Repository = Repo,
        IssueNumber = 123,
        ActorType = "Agent",
        ActorName = agentName,
        Action = "Completed",
        Summary = $"{agentName} done",
        Decision = "ContinueAutonomously",
        RiskLevel = "Low"
    };
}
