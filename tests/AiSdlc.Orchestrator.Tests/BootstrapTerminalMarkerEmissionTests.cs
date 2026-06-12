using AiSdlc.Agents;
using AiSdlc.Audit;
using AiSdlc.Events.Contract;
using AiSdlc.Events.Contract.Data;
using AiSdlc.GitHub;
using AiSdlc.Orchestrator.Events;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.RepoIndex;
using AiSdlc.RepoIndex.Charter;
using AiSdlc.Shared;
using AiSdlc.Shared.AutoMerge;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class BootstrapTerminalMarkerEmissionTests
{
    private const string Repository = "kcsnap/launchcart";
    private const int IssueNumber = 99;
    private static readonly string RunId = $"{Repository.Replace('/', '_')}_{IssueNumber}";

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task Activity_WritesWorkflowActorAuditEventWithExpectedShape(string status)
    {
        var audit = new InMemoryAuditService();
        var functions = BuildFunctions(audit);

        await functions.EmitBootstrapTerminalMarkerAuditAsync(
            new BootstrapTerminalMarkerAuditInput(Repository, IssueNumber, status),
            CancellationToken.None);

        var events = await audit.GetByRunIdAsync(RunId, CancellationToken.None);
        var emitted = Assert.Single(events);

        Assert.Equal("Workflow", emitted.ActorType);
        Assert.Equal("Orchestrator", emitted.ActorName);
        Assert.Equal("BootstrapTerminalMarker", emitted.Action);
        Assert.Equal(status, emitted.Decision);
        Assert.Equal($"Bootstrap run {status}.", emitted.Summary);
        Assert.Equal(Repository, emitted.Repository);
        Assert.Equal(IssueNumber, emitted.IssueNumber);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task EmittedEvent_RoundTripsThroughMapper_AsBootstrapTerminalMarkerData(string status)
    {
        var audit = new InMemoryAuditService();
        var functions = BuildFunctions(audit);

        await functions.EmitBootstrapTerminalMarkerAuditAsync(
            new BootstrapTerminalMarkerAuditInput(Repository, IssueNumber, status),
            CancellationToken.None);

        var stored = await audit.GetByRunIdAfterRowKeyAsync(RunId, rowKeyExclusive: null, maxResults: 10, CancellationToken.None);
        var single = Assert.Single(stored);

        var envelope = AuditEventMapper.TryMap(single);

        Assert.NotNull(envelope);
        Assert.Equal(EventType.BootstrapTerminalMarker, envelope!.EventType);
        var data = Assert.IsType<BootstrapTerminalMarkerData>(envelope.Data);
        Assert.Equal(status, data.Status);
    }

    [Fact]
    public async Task Activity_DoesNotThrow_WhenAuditWriteFails()
    {
        var functions = BuildFunctions(new ThrowingAuditService());

        // Symmetric with RecordWorkflowExitAsync — storage failures are swallowed so a transient
        // audit-write blip can't break the orchestrator's terminal step.
        await functions.EmitBootstrapTerminalMarkerAuditAsync(
            new BootstrapTerminalMarkerAuditInput(Repository, IssueNumber, "completed"),
            CancellationToken.None);
    }

    private static AgentActivityFunctions BuildFunctions(IAuditService audit) => new(
        new NoopRunner(),
        new StubGitHubService(),
        new StubRepoIndexer(),
        new StubCharterReader(),
        new AutoMergeEligibilityService(),
        new PassthroughContextStore(),
        audit,
        new NoopBlobPromptStore(),
        NullLogger<AgentActivityFunctions>.Instance);

    private sealed class NoopRunner : IAgentRunner
    {
        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class StubGitHubService : IGitHubService
    {
        public Task<IssueDetails> GetIssueAsync(string r, int i, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string r, int i, CancellationToken ct) => throw new NotImplementedException();
        public Task<IssueComment> AddIssueCommentAsync(string r, int i, string m, CancellationToken ct) => throw new NotImplementedException();
        public Task<IssueComment> AddPullRequestCommentAsync(string r, int p, string m, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> AddLabelsAsync(string r, int n, IReadOnlyList<string> l, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> RemoveLabelsAsync(string r, int n, IReadOnlyList<string> l, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubPullRequestReference> CreatePullRequestAsync(CreatePullRequestRequest r, CancellationToken ct) => throw new NotImplementedException();
        public Task<PullRequestDetails> GetPullRequestAsync(string r, int p, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string r, int p, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string r, string s, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetFileContentAsync(string r, string p, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetBranchFileContentAsync(string r, string p, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<RepoTreeEntry>> GetBranchFileTreeAsync(string repository, string branch, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RepoTreeEntry>>([]);
        public Task MergePullRequestAsync(string r, int p, string m, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> GetDefaultBranchAsync(string r, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> GetDefaultBranchShaAsync(string r, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateBranchAsync(string r, string b, string s, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateOrUpdateFileAsync(string r, string p, string c, string m, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrgIssueSearchHit>> SearchOpenOrgIssuesByLabelAsync(string o, string l, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubRepoIndexer : IRepoIndexer
    {
        public Task<RepoIndex.RepoIndex?> IndexAsync(string repository, CancellationToken ct) =>
            Task.FromResult<RepoIndex.RepoIndex?>(null);
    }

    private sealed class StubCharterReader : ICharterReader
    {
        public Task<Charter?> ReadAsync(string repository, CancellationToken ct) =>
            Task.FromResult<Charter?>(null);
    }

    private sealed class PassthroughContextStore : IContextStore
    {
        public Task<string> OffloadAsync(string runId, string key, string content, CancellationToken ct) =>
            Task.FromResult($"ctx:{runId}/{key}");
        public Task<string> ResolveAsync(string reference, CancellationToken ct) =>
            Task.FromResult(reference);
        public bool IsReference(string? value) => false;
    }

    private sealed class NoopBlobPromptStore : IBlobPromptStore
    {
        public Task StoreAsync(string runId, string agentName, string prompt, string response, CancellationToken ct) =>
            Task.CompletedTask;
        public Task<PromptRecord?> GetAsync(string runId, string agentName, CancellationToken ct) =>
            Task.FromResult<PromptRecord?>(null);
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken ct) =>
            throw new InvalidOperationException("audit storage down");
        public Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<AuditEvent>> GetSinceAsync(DateTimeOffset since, int maxResults, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<StoredAuditEvent>> GetByRunIdAfterRowKeyAsync(string runId, string? rowKeyExclusive, int maxResults, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAuditEvent>>([]);
    }
}
