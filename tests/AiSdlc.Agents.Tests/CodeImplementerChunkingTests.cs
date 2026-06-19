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
    public async Task Scaffold_contract_reaches_greenfield_generation_prompts()
    {
        // Greenfield never sees the seeded shell — the contract must be injected so the implementer
        // fills feature slots instead of re-authoring auth / the API client / the DI seam.
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        await new CodeImplementerAgent(provider).ExecuteAsync(MakeRequest(), CancellationToken.None);

        var contract = provider.Requests[0].ContextDocuments[CodeImplementerAgent.ScaffoldContractLabel];
        Assert.Contains("AppShell", contract);                          // shell is immutable, already exists
        Assert.Contains("data-testid=\"signed-in\"", contract);
        Assert.Contains("Api.Features", contract);                      // backend seam namespace
        Assert.Contains("AddFeatures(IServiceCollection", contract);    // backend seam signature
        Assert.Contains("routes.tsx", contract);                        // a feature slot to fill
        Assert.Contains("@/lib/api", contract);                         // use the existing client
        Assert.Contains("Microsoft.Azure.Cosmos", contract);            // pin the Cosmos API (v005 fix)
        Assert.Contains("acceptance.spec.ts", contract);                // implementer authors the acceptance tests
        Assert.Contains("RepositoryBase", contract);                    // data-access base (v006)
        Assert.Contains("CANNOT add NuGet packages", contract);         // no unreferenced deps (v006 SendGrid)
        Assert.Contains("stub", contract);                              // stub external integrations
        Assert.Contains("MUTABLE POCOs", contract);                     // model convention (v007 CS8852)
        Assert.Contains("agrees with itself", contract);                // self-consistency (v007 CS1061)
        Assert.Contains("components/ui/", contract);                    // shipped shadcn palette (template #7)
        Assert.Contains("Api.Email.IEmailSender", contract);            // use shipped email, don't stub
        // and it travels into the batch prompts too
        Assert.True(provider.Requests[1].ContextDocuments.ContainsKey(CodeImplementerAgent.ScaffoldContractLabel));
    }

    [Fact]
    public async Task Static_profile_selects_the_static_scaffold_contract()
    {
        // A Static app has no React/Functions/Cosmos shell — it must get the Static contract, never the
        // FullStack one (whose RepositoryBase/AppShell/FeatureRegistration guidance would mislead it).
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        var request = MakeRequest();
        request.Context.Metadata["stackProfile"] = "Static";
        await new CodeImplementerAgent(provider).ExecuteAsync(request, CancellationToken.None);

        var contract = provider.Requests[0].ContextDocuments[CodeImplementerAgent.ScaffoldContractLabel];
        Assert.Contains("STATIC site", contract);
        Assert.Contains("index.html", contract);
        Assert.DoesNotContain("RepositoryBase", contract);     // no Cosmos data layer
        Assert.DoesNotContain("AppShell", contract);           // no React shell
        Assert.DoesNotContain("FeatureRegistration", contract); // no backend DI seam
    }

    [Fact]
    public async Task NeedsAuth_false_selects_the_no_auth_scaffold_contract()
    {
        // Yorrixx seeds a no-auth shell when the charter says NeedsAuth == false; the contract must
        // match — never claim Clerk/auth is wired (the mismatch this conditionalisation fixes).
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        var request = MakeRequest();
        request.Context.Metadata["needsAuth"] = "false";
        await new CodeImplementerAgent(provider).ExecuteAsync(request, CancellationToken.None);

        var contract = provider.Requests[0].ContextDocuments[CodeImplementerAgent.ScaffoldContractLabel];
        Assert.Contains("THIS APP HAS NO AUTHENTICATION", contract);
        Assert.Contains("data-testid=\"app-ready\"", contract);
        // never CLAIM auth is wired (the doc may still name Clerk inside a prohibition)
        Assert.DoesNotContain("AUTHENTICATION IS ALREADY DONE", contract);
        Assert.DoesNotContain("wraps the app in <ClerkProvider>", contract);
        Assert.DoesNotContain("data-testid=\"signed-in\"", contract);   // no signed-in gate
        Assert.DoesNotContain("src/api/Auth/**", contract);             // no Auth/ in the no-auth shell
        // shared, auth-agnostic guidance still travels
        Assert.Contains("Api.Features", contract);
        Assert.Contains("RepositoryBase", contract);
        Assert.Contains("Api.Email.IEmailSender", contract);
    }

    [Fact]
    public async Task NeedsAuth_true_and_absent_both_select_the_auth_scaffold_contract()
    {
        // Explicit true → auth doc; absent (no charter / off-path context) → auth doc too (today's
        // behaviour). The real flow always sets needsAuth from the wizard-Required charter field.
        foreach (var value in new[] { "true", null })
        {
            var provider = new ScriptedModelProvider(
                _ => Manifest,
                req => FileBlocksFor(RequestedPaths(req)),
                req => FileBlocksFor(RequestedPaths(req)));

            var request = MakeRequest();
            if (value is not null) request.Context.Metadata["needsAuth"] = value;
            await new CodeImplementerAgent(provider).ExecuteAsync(request, CancellationToken.None);

            var contract = provider.Requests[0].ContextDocuments[CodeImplementerAgent.ScaffoldContractLabel];
            Assert.Contains("AUTHENTICATION IS ALREADY DONE", contract);
            Assert.Contains("data-testid=\"signed-in\"", contract);
            Assert.DoesNotContain("THIS APP HAS NO AUTHENTICATION", contract);
        }
    }

    [Fact]
    public async Task Manifest_prompt_lets_the_planner_author_acceptance_spec()
    {
        // The reframe excludes tests/e2e/ from the plan, but acceptance.spec.ts is the one file the
        // implementer must author over the seeded throwing stubs (v005 regression fix).
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        await new CodeImplementerAgent(provider).ExecuteAsync(MakeRequest(), CancellationToken.None);

        // Requests[0] is the manifest-planning call.
        var manifestPrompt = provider.Requests[0].SystemPrompt;
        Assert.Contains("tests/e2e/specs/acceptance.spec.ts", manifestPrompt);
        Assert.Contains("EXCEPT", manifestPrompt);  // tests/e2e/ excluded EXCEPT acceptance.spec.ts
    }

    [Fact]
    public async Task Scaffold_contract_defers_legal_to_the_shell()
    {
        // Legal links + pages now live in the shell footer (template-owned); the implementer must
        // not author them.
        var provider = new ScriptedModelProvider(
            _ => Manifest,
            req => FileBlocksFor(RequestedPaths(req)),
            req => FileBlocksFor(RequestedPaths(req)));

        await new CodeImplementerAgent(provider).ExecuteAsync(MakeRequest(), CancellationToken.None);

        var contract = provider.Requests[0].ContextDocuments[CodeImplementerAgent.ScaffoldContractLabel];
        Assert.Contains("Terms of Service", contract);
        Assert.Contains("footer", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do NOT add legal links", contract);
    }

    [Fact]
    public async Task Scaffold_contract_reaches_repair_prompts()
    {
        var provider = new ScriptedModelProvider(
            _ => "<file path=\"src/App.tsx\">\nconst fixed = true;\n</file>");

        var request = MakeRequest();
        request.Context.Metadata["reopenFindings"] = "login flow broken";
        request.Context.Metadata["existingSource"] = "<file path=\"src/App.tsx\">\nconst broken = Foo;\n</file>";

        await new CodeImplementerAgent(provider).ExecuteAsync(request, CancellationToken.None);

        Assert.True(provider.Requests[0].ContextDocuments.ContainsKey(CodeImplementerAgent.ScaffoldContractLabel));
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
