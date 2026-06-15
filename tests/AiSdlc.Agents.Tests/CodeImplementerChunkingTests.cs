using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

public sealed class CodeImplementerChunkingTests
{
    private const string Manifest = """
        <manifest>
        <item path="package.json">npm manifest</item>
        <item path="src/index.ts">entrypoint</item>
        <item path="src/auth/authService.ts">authentication service</item>
        <item path="src/bookings/bookingService.ts">booking service</item>
        </manifest>
        """;

    [Fact]
    public void Manifest_parses_paths_and_purposes()
    {
        var items = CodeImplementerAgent.ParseManifest(Manifest);

        Assert.Equal(4, items.Count);
        Assert.Equal("package.json", items[0].Path);
        Assert.Equal("npm manifest", items[0].Purpose);
        Assert.Equal("src/bookings/bookingService.ts", items[3].Path);
    }

    [Fact]
    public void Manifest_dedupes_repeated_paths_and_tolerates_garbage()
    {
        Assert.Empty(CodeImplementerAgent.ParseManifest(null));
        Assert.Empty(CodeImplementerAgent.ParseManifest("no manifest here"));

        var items = CodeImplementerAgent.ParseManifest("""
            <manifest>
            <item path="a.ts">first</item>
            <item path="A.TS">duplicate of first</item>
            </manifest>
            """);
        Assert.Single(items);
    }

    [Fact]
    public async Task Chunked_generation_emits_every_manifest_file()
    {
        // 4 files at BatchSize 3 → manifest call + 2 batch calls.
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        var result = await new CodeImplementerAgent(provider)
            .ExecuteAsync(MakeRequest(), CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        var files = CodeChangeParser.Parse(result.OutputMarkdown);
        Assert.Equal(4, files.Count);
        Assert.Contains(files, f => f.Path == "src/bookings/bookingService.ts");
        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal("CodeImplementationManifest", provider.Requests[0].TaskType);
    }

    [Fact]
    public async Task Missing_file_is_recovered_by_individual_retry()
    {
        // Batch 1 drops authService (simulating truncation); the recovery pass regenerates it alone.
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req).Where(p => !p.Contains("auth"))),
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        var result = await new CodeImplementerAgent(provider)
            .ExecuteAsync(MakeRequest(), CancellationToken.None);

        var files = CodeChangeParser.Parse(result.OutputMarkdown);
        Assert.Equal(4, files.Count);
        Assert.Contains(files, f => f.Path == "src/auth/authService.ts");

        var recovery = provider.Requests[^1];
        Assert.Contains("src/auth/authService.ts", recovery.UserPrompt);
    }

    [Fact]
    public async Task Still_missing_after_recovery_fails_the_stage()
    {
        // authService never materializes — the stage must fail rather than commit a partial app.
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req).Where(p => !p.Contains("auth"))),
            req => FileBlocksFor(RequestedPaths(req)),
            _ => "I cannot generate that file.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CodeImplementerAgent(provider).ExecuteAsync(MakeRequest(), CancellationToken.None));

        Assert.Contains("src/auth/authService.ts", ex.Message);
    }

    [Fact]
    public async Task Runaway_manifest_is_refused()
    {
        var huge = "<manifest>\n" + string.Join("\n", Enumerable.Range(0, CodeImplementerAgent.ManifestFileCap + 1)
            .Select(i => $"<item path=\"src/f{i}.ts\">file {i}</item>")) + "\n</manifest>";
        var provider = new ScriptedModelProvider(_ => huge);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CodeImplementerAgent(provider).ExecuteAsync(MakeRequest(), CancellationToken.None));

        Assert.Contains("runaway", ex.Message);
    }

    [Theory]
    [InlineData("reopenFindings", null, false)]              // findings without source — no repair
    [InlineData(null, null, false)]                          // neither
    [InlineData("reopenFindings", "existingSource", true)]   // reopen repair
    [InlineData("ciFindings", "existingSource", true)]       // in-run CI repair
    public void Repair_trigger_requires_findings_plus_source(string? findingsKey, string? sourceKey, bool expect)
    {
        var context = MakeRequest().Context;
        if (findingsKey is not null) context.Metadata[findingsKey] = "some findings";
        if (sourceKey is not null) context.Metadata[sourceKey] = "<file path=\"a\">x</file>";
        Assert.Equal(expect, CodeImplementerAgent.IsRepairRequest(context));
    }

    [Fact]
    public async Task Ci_findings_trigger_the_same_surgical_repair_path()
    {
        var provider = new ScriptedModelProvider(
            _ => "<file path=\"src/App.tsx\">\nconst fixed = true;\n</file>");

        var request = MakeRequest();
        request.Context.Metadata["ciFindings"]     = "## Check: build-frontend\nsrc/App.tsx:3 [failure] TS2304";
        request.Context.Metadata["existingSource"] = "<file path=\"src/App.tsx\">\nconst broken = Foo;\n</file>";

        var result = await new CodeImplementerAgent(provider).ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Single(provider.Requests);
        Assert.Equal("CodeRepair", provider.Requests[0].TaskType);
        Assert.Contains(provider.Requests[0].ContextDocuments.Keys,
            k => k == AgentContextDocuments.CiFindingsDocumentName);
    }

    [Fact]
    public async Task Repair_mode_makes_one_surgical_call_and_never_regenerates()
    {
        // With findings + existing source present, the agent must take the repair path:
        // a single CodeRepair call (no manifest, no chunked regeneration).
        var provider = new ScriptedModelProvider(
            _ => "<file path=\"src/App.tsx\">\nconst fixed = true;\n</file>");

        var request = MakeRequest();
        request.Context.Metadata["reopenFindings"] = "TS2304: Cannot find name 'Foo' in src/App.tsx";
        request.Context.Metadata["existingSource"] = "<file path=\"src/App.tsx\">\nconst broken = Foo;\n</file>";

        var result = await new CodeImplementerAgent(provider).ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Single(provider.Requests);
        Assert.Equal("CodeRepair", provider.Requests[0].TaskType);
        Assert.Contains("Existing Source", provider.Requests[0].ContextDocuments.Keys.Single(k => k.StartsWith("Existing")));
        Assert.Single(CodeChangeParser.Parse(result.OutputMarkdown));
        Assert.Contains("1 file(s)", result.Summary);
    }

    [Fact]
    public async Task Auth_contract_reaches_greenfield_generation_prompts()
    {
        // Greenfield never sees the seeded Clerk scaffold — the contract must be injected so the
        // implementer doesn't replace the Clerk auth shell with a custom LoginPage.
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        await new CodeImplementerAgent(provider).ExecuteAsync(MakeRequest(), CancellationToken.None);

        var contract = provider.Requests[0].ContextDocuments[CodeImplementerAgent.AuthContractLabel];
        Assert.Contains("ClerkProvider", contract);
        Assert.Contains("data-testid=\"signed-in\"", contract);
        Assert.Contains(".cl-formButtonPrimary", contract);
        // and it travels into the batch prompts too
        Assert.True(provider.Requests[1].ContextDocuments.ContainsKey(CodeImplementerAgent.AuthContractLabel));
    }

    [Fact]
    public async Task Auth_contract_reaches_repair_prompts()
    {
        var provider = new ScriptedModelProvider(
            _ => "<file path=\"src/App.tsx\">\nconst fixed = true;\n</file>");

        var request = MakeRequest();
        request.Context.Metadata["reopenFindings"] = "login flow broken";
        request.Context.Metadata["existingSource"] = "<file path=\"src/App.tsx\">\nconst broken = Foo;\n</file>";

        await new CodeImplementerAgent(provider).ExecuteAsync(request, CancellationToken.None);

        Assert.True(provider.Requests[0].ContextDocuments.ContainsKey(CodeImplementerAgent.AuthContractLabel));
    }

    [Fact]
    public async Task No_manifest_falls_back_to_single_shot()
    {
        var provider = new ScriptedModelProvider(
            _ => "no manifest, model ignored the format",
            _ => "<file path=\"a.ts\">\nconst a = 1;\n</file>");

        var result = await new CodeImplementerAgent(provider)
            .ExecuteAsync(MakeRequest(), CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Single(CodeChangeParser.Parse(result.OutputMarkdown));
        Assert.Equal(2, provider.Requests.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> RequestedPaths(ModelRequest req)
    {
        // The batch prompt lists requested files after the "Generate ONLY these files" line.
        var marker = req.UserPrompt.IndexOf("Generate ONLY these files", StringComparison.Ordinal);
        var section = marker >= 0 ? req.UserPrompt[marker..] : req.UserPrompt;
        return section.Split('\n')
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..].Split(" — ")[0].Trim());
    }

    private static string FileBlocksFor(IEnumerable<string> paths) =>
        string.Join("\n", paths.Select(p => $"<file path=\"{p}\">\n// {p}\n</file>"));

    private static AgentExecutionRequest MakeRequest() => new()
    {
        AgentName = AgentNames.CodeImplementer,
        Context = new AgentContext
        {
            RunId          = "run-1",
            Repository     = "yorrixx-apps/user-app-test",
            IssueNumber    = 1,
            CurrentState   = "Started",
            RequestedAgent = AgentNames.CodeImplementer,
            Metadata       =
            {
                ["issueTitle"] = "Build app test",
                ["issueBody"]  = "Build the MVP."
            }
        }
    };

    private sealed class ScriptedModelProvider : IModelProvider
    {
        private readonly Func<ModelRequest, string>[] _scripts;

        public ScriptedModelProvider(params Func<ModelRequest, string>[] scripts) => _scripts = scripts;

        public List<ModelRequest> Requests { get; } = [];

        public string ProviderName => "Scripted";

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Requests.Count > _scripts.Length)
                throw new InvalidOperationException($"Unexpected model call #{Requests.Count} ({request.TaskType}).");

            return Task.FromResult(new ModelResponse
            {
                ProviderName = ProviderName,
                ModelName    = "scripted",
                ResponseText = _scripts[Requests.Count - 1](request)
            });
        }
    }
}
