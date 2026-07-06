using AiSdlc.Agents;
using AiSdlc.Audit;
using AiSdlc.GitHub;
using AiSdlc.ModelProviders;
using AiSdlc.Orchestrator.Functions;
using AiSdlc.Orchestrator.Imagery;
using AiSdlc.RepoIndex;
using AiSdlc.Shared;
using AiSdlc.Shared.AutoMerge;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class AgentActivityAuditTests
{
    [Fact]
    public async Task SuccessPath_WritesStartedThenCompletedAuditEvents()
    {
        var audit  = new InMemoryAuditService();
        var runner = new SucceedingRunner(new AgentResult
        {
            AgentName = "ProductStrategist",
            Status    = "Completed",
            Summary   = "Strategy aligned with roadmap",
            Decision  = "Approve",
            RiskLevel = "Low"
        });
        var functions = BuildFunctions(audit, runner);

        var ctx = MakeContext();

        var result = await functions.RunProductStrategistAsync(ctx, CancellationToken.None);
        Assert.Equal("ProductStrategist", result.AgentName);

        var events = await audit.GetByRunIdAsync(ctx.RunId, CancellationToken.None);
        Assert.Equal(2, events.Count);

        // Order matters: Started first, Completed second.
        var ordered = events.OrderBy(e => e.TimestampUtc).ToArray();
        Assert.Equal("Started",   ordered[0].Action);
        Assert.Equal("Completed", ordered[1].Action);
        Assert.Equal("Agent",     ordered[1].ActorType);
        Assert.Equal("Strategy aligned with roadmap", ordered[1].Summary);
        Assert.Equal("Approve",   ordered[1].Decision);
        Assert.Equal("Low",       ordered[1].RiskLevel);
    }

    [Fact]
    public async Task FailurePath_WritesStartedAndFailedThenRethrows()
    {
        var audit  = new InMemoryAuditService();
        var runner = new ThrowingRunner(new HttpRequestException("Anthropic API returned 429"));
        var functions = BuildFunctions(audit, runner);

        var ctx = MakeContext();

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() =>
            functions.RunProductStrategistAsync(ctx, CancellationToken.None));
        Assert.Equal("Anthropic API returned 429", thrown.Message);

        var events = (await audit.GetByRunIdAsync(ctx.RunId, CancellationToken.None))
            .OrderBy(e => e.TimestampUtc)
            .ToArray();

        Assert.Equal(2, events.Length);
        Assert.Equal("Started", events[0].Action);

        var failed = events[1];
        Assert.Equal("Failed", failed.Action);
        Assert.Equal("Agent",  failed.ActorType);
        Assert.Equal("Anthropic API returned 429", failed.Summary);
        Assert.Equal("System.Net.Http.HttpRequestException", failed.References["exceptionType"]);
        Assert.Contains("HttpRequestException", failed.References["stackTrace"]);
    }

    [Fact]
    public async Task FailurePath_AuditWriteException_DoesNotMaskOriginalAgentException()
    {
        var audit  = new AlwaysThrowingAuditService();
        var runner = new ThrowingRunner(new InvalidOperationException("agent blew up"));
        var functions = BuildFunctions(audit, runner);

        var ctx = MakeContext();

        // The original agent exception must propagate even though the audit write also failed.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            functions.RunProductStrategistAsync(ctx, CancellationToken.None));
        Assert.Equal("agent blew up", thrown.Message);
    }

    [Fact]
    public async Task RecordWorkflowExitAsync_WritesWorkflowActorAuditEvent()
    {
        var audit  = new InMemoryAuditService();
        var runner = new SucceedingRunner(new AgentResult { AgentName = "noop", Status = "Completed", Summary = "noop" });
        var functions = BuildFunctions(audit, runner);

        var input = new AiSdlc.Orchestrator.WorkflowExitAuditInput(
            Repository: "org/repo",
            IssueNumber: 9,
            Outcome:    "Stopped",
            Reason:     "Code implementer produced no file changes");

        await functions.RecordWorkflowExitAsync(input, CancellationToken.None);

        var events = await audit.GetByRunIdAsync("org_repo_9", CancellationToken.None);
        var ev = Assert.Single(events);
        Assert.Equal("Workflow",     ev.ActorType);
        Assert.Equal("Orchestrator", ev.ActorName);
        Assert.Equal("Stopped",      ev.Action);
        Assert.Equal("Code implementer produced no file changes", ev.Summary);
        Assert.Equal("Stopped",      ev.Decision);
    }

    [Fact]
    public async Task SuccessPath_StoresAgentInputAndOutputToBlobPromptStore()
    {
        const string expectedOutput = "## Strategy\n\nGo to market via Q3 launch.";
        var audit  = new InMemoryAuditService();
        var store  = new RecordingBlobPromptStore();
        var runner = new SucceedingRunner(new AgentResult
        {
            AgentName      = "ProductStrategist",
            Status         = "Completed",
            Summary        = "Strategy aligned",
            OutputMarkdown = expectedOutput
        });
        var functions = BuildFunctions(audit, runner, store);

        var ctx = new AgentContext
        {
            RunId          = "run-audit-1",
            Repository     = "org/repo",
            IssueNumber    = 42,
            CurrentState   = "Started",
            RequestedAgent = AgentNames.ProductStrategist,
            Metadata       =
            {
                ["issueTitle"] = "Add dark mode",
                ["issueBody"]  = "We want a dark mode toggle on the settings page."
            }
        };

        await functions.RunProductStrategistAsync(ctx, CancellationToken.None);

        var stored = Assert.Single(store.Stored);
        Assert.Equal("run-audit-1",       stored.RunId);
        Assert.Equal(AgentNames.ProductStrategist, stored.AgentName);
        Assert.Contains("Add dark mode",  stored.Prompt);     // metadata serialised into input
        Assert.Contains("dark mode toggle", stored.Prompt);   // value text included
        Assert.Equal(expectedOutput, stored.Response);
    }

    [Fact]
    public async Task FailurePath_DoesNotStoreBlobArtefact()
    {
        var audit  = new InMemoryAuditService();
        var store  = new RecordingBlobPromptStore();
        var runner = new ThrowingRunner(new InvalidOperationException("agent crashed"));
        var functions = BuildFunctions(audit, runner, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            functions.RunProductStrategistAsync(MakeContext(), CancellationToken.None));

        Assert.Empty(store.Stored);
    }

    [Fact]
    public async Task SuccessPath_BlobStoreFailureIsSwallowed()
    {
        var audit  = new InMemoryAuditService();
        var runner = new SucceedingRunner(new AgentResult
        {
            AgentName      = "ProductStrategist",
            Status         = "Completed",
            Summary        = "ok",
            OutputMarkdown = "result"
        });
        var functions = BuildFunctions(audit, runner, new ThrowingBlobPromptStore());

        // Should complete normally even though the prompt-store write throws.
        var result = await functions.RunProductStrategistAsync(MakeContext(), CancellationToken.None);
        Assert.Equal("ProductStrategist", result.AgentName);

        // And the regular Completed audit event still gets written.
        var events = await audit.GetByRunIdAsync("run-audit-1", CancellationToken.None);
        Assert.Contains(events, e => e.Action == "Completed");
    }

    [Fact]
    public async Task SuccessPath_LongOutputMarkdown_StillWritesCompletedAudit()
    {
        var audit  = new InMemoryAuditService();
        var runner = new SucceedingRunner(new AgentResult
        {
            AgentName      = "ProductStrategist",
            Status         = "Completed",
            Summary        = "Big output",
            OutputMarkdown = new string('x', 2048)  // triggers blob offload branch in ExecuteAsync
        });
        var functions = BuildFunctions(audit, runner);

        await functions.RunProductStrategistAsync(MakeContext(), CancellationToken.None);

        var events = await audit.GetByRunIdAsync("run-audit-1", CancellationToken.None);
        Assert.Contains(events, e => e.Action == "Started");
        Assert.Contains(events, e => e.Action == "Completed");
    }

    private static AgentContext MakeContext() => new()
    {
        RunId          = "run-audit-1",
        Repository     = "org/repo",
        IssueNumber    = 9,
        CurrentState   = "Started",
        RequestedAgent = AgentNames.ProductStrategist
    };

    private static AgentActivityFunctions BuildFunctions(
        IAuditService audit,
        IAgentRunner runner,
        IBlobPromptStore? promptStore = null) => new(
        runner,
        new StubGitHubService(),
        new StubRepoIndexer(),
        new StubCharterReader(),
        new AutoMergeEligibilityService(),
        new PassthroughContextStore(),
        audit,
        promptStore ?? new RecordingBlobPromptStore(),
        new FakeModelProvider(new ModelProviderOptions { ProviderName = "Fake", ModelName = "fake-model" }),
        new NoOpImageSource(),
        NullLogger<AgentActivityFunctions>.Instance);

    private sealed class RecordingBlobPromptStore : IBlobPromptStore
    {
        public List<(string RunId, string AgentName, string Prompt, string Response)> Stored { get; } = new();

        public Task StoreAsync(string runId, string agentName, string prompt, string response, CancellationToken ct)
        {
            Stored.Add((runId, agentName, prompt, response));
            return Task.CompletedTask;
        }

        public Task<PromptRecord?> GetAsync(string runId, string agentName, CancellationToken ct) =>
            Task.FromResult<PromptRecord?>(null);
    }

    private sealed class ThrowingBlobPromptStore : IBlobPromptStore
    {
        public Task StoreAsync(string runId, string agentName, string prompt, string response, CancellationToken ct) =>
            throw new InvalidOperationException("blob storage unavailable");

        public Task<PromptRecord?> GetAsync(string runId, string agentName, CancellationToken ct) =>
            Task.FromResult<PromptRecord?>(null);
    }

    private sealed class SucceedingRunner : IAgentRunner
    {
        private readonly AgentResult _result;
        public SucceedingRunner(AgentResult result) { _result = result; }
        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct) =>
            Task.FromResult(new AgentExecutionResult { Succeeded = true, AgentName = _result.AgentName, Result = _result });
    }

    private sealed class ThrowingRunner : IAgentRunner
    {
        private readonly Exception _ex;
        public ThrowingRunner(Exception ex) { _ex = ex; }
        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct) =>
            throw _ex;
    }

    private sealed class AlwaysThrowingAuditService : IAuditService
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken ct) =>
            throw new InvalidOperationException("audit storage down");
        public Task<IReadOnlyList<AuditEvent>> GetByRunIdAsync(string runId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<AuditEvent>> GetSinceAsync(DateTimeOffset since, int maxResults, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<StoredAuditEvent>> GetByRunIdAfterRowKeyAsync(
            string runId, string? rowKeyExclusive, int maxResults, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAuditEvent>>([]);
    }

    private sealed class PassthroughContextStore : IContextStore
    {
        public Task<string> OffloadAsync(string runId, string key, string content, CancellationToken ct) =>
            Task.FromResult($"ctx:{runId}/{key}");
        public Task<string> ResolveAsync(string reference, CancellationToken ct) =>
            Task.FromResult(reference);
        public bool IsReference(string? value) => false;
    }

    private sealed class StubRepoIndexer : IRepoIndexer
    {
        public Task<RepoIndex.RepoIndex?> IndexAsync(string repository, CancellationToken ct) =>
            Task.FromResult<RepoIndex.RepoIndex?>(null);
    }

    private sealed class StubCharterReader : RepoIndex.Charter.ICharterReader
    {
        public Task<Yorrixx.Contracts.Generation.Charter?> ReadAsync(string repository, CancellationToken ct) =>
            Task.FromResult<Yorrixx.Contracts.Generation.Charter?>(null);
    }

    // Minimal IGitHubService stub — these tests never hit the GitHub paths.
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
        public Task<IReadOnlyList<FailedCheckFinding>> GetFailedCheckFindingsAsync(string repository, string reference, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FailedCheckFinding>>([]);
        public Task<OpenPullRequestInfo?> GetNewestOpenPullRequestByBranchPrefixAsync(string repository, string branchPrefix, CancellationToken cancellationToken) => Task.FromResult<OpenPullRequestInfo?>(null);
        public Task MergePullRequestAsync(string r, int p, string m, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> GetDefaultBranchAsync(string r, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> GetDefaultBranchShaAsync(string r, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateBranchAsync(string r, string b, string s, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateOrUpdateFileAsync(string r, string p, string c, string m, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrgIssueSearchHit>> SearchOpenOrgIssuesByLabelAsync(string o, string l, CancellationToken ct) => throw new NotImplementedException();
        public Task<CreatedRepository> CreateRepositoryFromTemplateAsync(string t, string o, string n, bool p, string d, CancellationToken ct) => throw new NotImplementedException();
        public Task SetRepoVariableAsync(string r, string n, string v, CancellationToken ct) => throw new NotImplementedException();
    }
}
