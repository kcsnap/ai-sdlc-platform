using AiSdlc.Agents.Personas;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

/// <summary>
/// Regression tests for issue #41: every agent must thread Metadata["charter"]
/// into ContextDocuments["App Charter"] so user-app intent reaches the model.
/// </summary>
public sealed class CharterContextDocumentTests
{
    private const string SampleCharterMarkdown =
        "## App Charter (v1)\n\n**App:** TaskFlow\n**Description:** Personal task tracker.";

    public static IEnumerable<object[]> AllAgentFactories()
    {
        yield return Make(m => new ProductStrategistAgent(m));
        yield return Make(m => new ProductOwnerAgent(m));
        yield return Make(m => new BusinessAnalystAgent(m));
        yield return Make(m => new ProductOwnerBranchReviewAgent(m));
        yield return Make(m => new ArchitectAgent(m));
        yield return Make(m => new CodeImplementerAgent(m));
        yield return Make(m => new ComplianceLegalReviewerAgent(m));
        yield return Make(m => new ContentSeoReviewerAgent(m));
        yield return Make(m => new DataAnalyticsReviewerAgent(m));
        yield return Make(m => new DevOpsPlatformEngineerAgent(m));
        yield return Make(m => new QaTestEngineerAgent(m));
        yield return Make(m => new ReleaseManagerAgent(m));
        yield return Make(m => new RiskAssessorAgent(m));
        yield return Make(m => new SecurityPrivacyReviewerAgent(m));
        yield return Make(m => new SeniorCoderAgent(m));
        yield return Make(m => new UxAccessibilityReviewerAgent(m));

        static object[] Make(Func<IModelProvider, IAgent> factory) => new object[] { new AgentFactory(factory) };
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Agent_threads_charter_into_context_documents(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        await agent.ExecuteAsync(MakeRequest(agent.Name, withCharter: true), CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.True(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.CharterDocumentName),
            $"{agent.Name} did not include 'App Charter' in ContextDocuments.");
        Assert.Equal(SampleCharterMarkdown, recorder.LastRequest.ContextDocuments[AgentContextDocuments.CharterDocumentName]);
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Agent_omits_charter_when_metadata_absent(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        await agent.ExecuteAsync(MakeRequest(agent.Name, withCharter: false), CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.False(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.CharterDocumentName),
            $"{agent.Name} included 'App Charter' even though Metadata[\"charter\"] was absent.");
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Bootstrap_mode_adds_operating_mode_document(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        await agent.ExecuteAsync(MakeRequest(agent.Name, withCharter: true, mode: WorkflowMode.Bootstrap), CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.True(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.OperatingModeDocumentName),
            $"{agent.Name} did not include 'Operating Mode' in ContextDocuments under Bootstrap mode.");
        var operatingMode = recorder.LastRequest.ContextDocuments[AgentContextDocuments.OperatingModeDocumentName];
        Assert.Contains("BOOTSTRAP", operatingMode);
        Assert.Contains("Open Questions", operatingMode);
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Standard_mode_omits_operating_mode_document(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        await agent.ExecuteAsync(MakeRequest(agent.Name, withCharter: true, mode: WorkflowMode.Standard), CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.False(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.OperatingModeDocumentName),
            $"{agent.Name} included 'Operating Mode' in Standard mode — it should only appear for Bootstrap.");
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task NoAuth_charter_adds_authentication_posture_document(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        var request = MakeRequest(agent.Name, withCharter: true);
        request.Context.Metadata["needsAuth"] = "false";
        await agent.ExecuteAsync(request, CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.True(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.AuthPostureDocumentName),
            $"{agent.Name} did not include 'Authentication Posture' for a NeedsAuth=false app.");
        var posture = recorder.LastRequest.ContextDocuments[AgentContextDocuments.AuthPostureDocumentName];
        Assert.Contains("NO AUTHENTICATION", posture);
        Assert.Contains("auth.spec.ts", posture);
        Assert.Contains("[AllowAnonymous]", posture);   // v011: backend authz-attribute reflex (#149)
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Auth_charter_and_absent_flag_omit_authentication_posture_document(AgentFactory factory)
    {
        foreach (var needsAuth in new[] { "true", null })
        {
            var recorder = new RecordingModelProvider();
            var agent    = factory.Build(recorder);

            var request = MakeRequest(agent.Name, withCharter: true);
            if (needsAuth is not null) request.Context.Metadata["needsAuth"] = needsAuth;
            await agent.ExecuteAsync(request, CancellationToken.None);

            Assert.NotNull(recorder.LastRequest);
            Assert.False(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.AuthPostureDocumentName),
                $"{agent.Name} included 'Authentication Posture' when needsAuth={needsAuth ?? "absent"} — it must only appear for an explicit false.");
        }
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Static_profile_adds_stack_profile_posture_document(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        var request = MakeRequest(agent.Name, withCharter: true);
        request.Context.Metadata["stackProfile"] = "Static";
        await agent.ExecuteAsync(request, CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.True(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.StackProfilePostureDocumentName),
            $"{agent.Name} did not include 'Stack Profile Posture' for a Static app.");
        var posture = recorder.LastRequest.ContextDocuments[AgentContextDocuments.StackProfilePostureDocumentName];
        Assert.Contains("STATIC site", posture);
        Assert.Contains("React", posture);   // prohibited for static
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task FullStack_and_absent_profile_omit_stack_profile_posture(AgentFactory factory)
    {
        foreach (var profile in new[] { "FullStack", null })
        {
            var recorder = new RecordingModelProvider();
            var agent    = factory.Build(recorder);

            var request = MakeRequest(agent.Name, withCharter: true);
            if (profile is not null) request.Context.Metadata["stackProfile"] = profile;
            await agent.ExecuteAsync(request, CancellationToken.None);

            Assert.NotNull(recorder.LastRequest);
            Assert.False(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.StackProfilePostureDocumentName),
                $"{agent.Name} included 'Stack Profile Posture' when stackProfile={profile ?? "absent"} — only an explicit Static should.");
        }
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task ApiOnly_profile_adds_capability_posture_document(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        var request = MakeRequest(agent.Name, withCharter: true);
        request.Context.Metadata["needsDatabase"] = "false";
        await agent.ExecuteAsync(request, CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.True(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.CapabilityPostureDocumentName),
            $"{agent.Name} did not include 'Capability Posture' for an api-only (needsDatabase=false) app.");
        var posture = recorder.LastRequest.ContextDocuments[AgentContextDocuments.CapabilityPostureDocumentName];
        Assert.Contains("API-ONLY", posture);
        Assert.Contains("RepositoryBase", posture);   // the DB seam it must NOT use
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Database_profile_and_absent_flag_omit_capability_posture(AgentFactory factory)
    {
        foreach (var needsDatabase in new[] { "true", null })
        {
            var recorder = new RecordingModelProvider();
            var agent    = factory.Build(recorder);

            var request = MakeRequest(agent.Name, withCharter: true);
            if (needsDatabase is not null) request.Context.Metadata["needsDatabase"] = needsDatabase;
            await agent.ExecuteAsync(request, CancellationToken.None);

            Assert.NotNull(recorder.LastRequest);
            Assert.False(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.CapabilityPostureDocumentName),
                $"{agent.Name} included 'Capability Posture' when needsDatabase={needsDatabase ?? "absent"} — only an explicit false should.");
        }
    }

    [Theory]
    [MemberData(nameof(AllAgentFactories))]
    public async Task Capability_gaps_metadata_adds_gaps_document(AgentFactory factory)
    {
        var recorder = new RecordingModelProvider();
        var agent    = factory.Build(recorder);

        var request = MakeRequest(agent.Name, withCharter: true);
        request.Context.Metadata["capabilityGaps"] = "- Email: payments on, email off";
        await agent.ExecuteAsync(request, CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.True(recorder.LastRequest!.ContextDocuments.ContainsKey(AgentContextDocuments.CapabilityGapsDocumentName),
            $"{agent.Name} did not surface capability gaps when capabilityGaps metadata was present.");
        Assert.Contains("payments on, email off",
            recorder.LastRequest.ContextDocuments[AgentContextDocuments.CapabilityGapsDocumentName]);
    }

    /// <summary>
    /// The Code Implementer must thread the UX agent's output (uxOutput) into its context so the
    /// Design Direction reaches the coder (static-design-quality.md §1). This was the load-bearing
    /// gap: BuildContextDocs threaded every other upstream output but not uxOutput, so a visual
    /// identity could be authored upstream and never seen by the agent that builds the page.
    /// </summary>
    [Fact]
    public async Task CodeImplementer_threads_ux_output_into_context_documents()
    {
        var recorder = new RecordingModelProvider();
        var agent    = new CodeImplementerAgent(recorder);

        await agent.ExecuteAsync(MakeRequest(agent.Name, withCharter: true), CancellationToken.None);

        Assert.NotNull(recorder.LastRequest);
        Assert.True(
            recorder.LastRequest!.ContextDocuments.ContainsKey("UX/UI Design Direction & Accessibility"),
            "CodeImplementer did not thread uxOutput into its context documents — the Design Direction never reaches the coder.");
        Assert.Equal("UX.", recorder.LastRequest.ContextDocuments["UX/UI Design Direction & Accessibility"]);
    }

    private static AgentExecutionRequest MakeRequest(string agentName, bool withCharter, WorkflowMode mode = WorkflowMode.Standard)
    {
        var metadata = new Dictionary<string, object>
        {
            ["issueTitle"]      = "Add search to product list",
            ["issueBody"]       = "Users need search and filter on /products page.",
            ["issueAuthor"]     = "testuser",
            ["repoContext"]     = "## Repo",
            ["strategistOutput"] = "Strategy.",
            ["ownerBrief"]      = "Brief.",
            ["analystOutput"]   = "BA analysis.",
            ["architectOutput"] = "Arch.",
            ["securityOutput"]  = "Sec.",
            ["uxOutput"]        = "UX.",
            ["devopsOutput"]    = "DevOps.",
            ["contentOutput"]   = "Content.",
            ["complianceOutput"] = "Compliance.",
            ["analyticsOutput"] = "Analytics.",
            ["testPlan"]        = "Test plan.",
            ["implSpec"]        = "Impl spec.",
            ["riskAssessment"]  = "Risk.",
            ["branchName"]      = "ai/1-foo",
            ["branchContent"]   = "### file.txt\nfoo"
        };

        if (withCharter)
            metadata["charter"] = SampleCharterMarkdown;

        return new AgentExecutionRequest
        {
            AgentName = agentName,
            Context   = new AgentContext
            {
                RunId          = "run-test",
                Repository     = "yorrixx-apps/user-app-abc12345",
                IssueNumber    = 1,
                CurrentState   = "Started",
                RequestedAgent = agentName,
                Mode           = mode,
                Metadata       = metadata
            }
        };
    }

    // A reference-typed wrapper so Theory MemberData can serialise it (Func<> alone isn't xUnit-friendly).
    public sealed class AgentFactory
    {
        private readonly Func<IModelProvider, IAgent> _build;
        public AgentFactory(Func<IModelProvider, IAgent> build) { _build = build; }
        public IAgent Build(IModelProvider m) => _build(m);
        public override string ToString() => _build(new RecordingModelProvider()).Name;
    }

    private sealed class RecordingModelProvider : IModelProvider
    {
        public ModelRequest? LastRequest { get; private set; }
        public string ProviderName => "Recorder";

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ModelResponse
            {
                ProviderName = "Recorder",
                ModelName    = "recorder-model",
                ResponseText = $"APPROVED\n[{request.AgentName}] recorded.",
                Usage        = new Dictionary<string, object>(),
                WasTruncated = false
            });
        }
    }
}
