using AiSdlc.GitHub;
using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class CiRepairLoopTests
{
    [Theory]
    [InlineData(1, 0, 0, true)]   // concrete failure, attempts available
    [InlineData(1, 0, 2, true)]   // mid-budget (MaxCiRepairAttempts is 6) — still actionable
    [InlineData(1, 0, 5, true)]   // still one attempt left
    [InlineData(1, 0, 6, false)]  // attempts exhausted
    [InlineData(0, 2, 0, false)]  // pure pending-timeout — no findings to act on
    [InlineData(1, 1, 0, true)]   // fast-fail build + slow e2e at budget — actionable
    [InlineData(0, 0, 0, false)]  // nothing failed
    public void Repair_attempted_only_on_concrete_failures_within_budget(int failed, int pending, int attemptsUsed, bool expect)
    {
        var state = new ChecksState(failed + pending, pending,
            Enumerable.Range(0, failed).Select(i => $"check-{i}").ToList());
        Assert.Equal(expect, AiSdlcWorkflowOrchestrator.ShouldAttemptCiRepair(state, attemptsUsed));
    }

    [Fact]
    public void Repair_attempt_comment_is_a_summary_with_no_code()
    {
        var comment = AiSdlcWorkflowOrchestrator.BuildCiRepairAttemptComment(
            1, 2, 3, ["build-api", "build-frontend"],
            "yorrixx-apps/user-app-x", "main", "ai/1-build", "abc123def456789");

        Assert.StartsWith("## AI SDLC", comment);
        Assert.Contains("attempt 1 of 2", comment);
        Assert.Contains("build-api", comment);
        Assert.Contains("3 file(s)", comment);
        Assert.Contains("abc123def456", comment);
        Assert.Contains("compare/main...ai/1-build", comment);
        Assert.DoesNotContain("<file", comment);
    }

    [Fact]
    public void Checks_failed_comment_mentions_repair_attempts_only_when_used()
    {
        var state = new ChecksState(2, 0, ["build-api"]);

        var without = AiSdlcWorkflowOrchestrator.BuildChecksFailedComment(state, "https://pr", 0);
        Assert.DoesNotContain("repair", without, StringComparison.OrdinalIgnoreCase);

        var with = AiSdlcWorkflowOrchestrator.BuildChecksFailedComment(state, "https://pr", 2);
        Assert.Contains("attempted 2 time(s)", with);
    }

    [Fact]
    public void Findings_render_annotations_as_path_line_level_message()
    {
        var findings = new List<FailedCheckFinding>
        {
            new("build-api",
                [new CheckAnnotation("src/api/Program.cs", 12, "failure", "CS0103: name 'Foo' does not exist")],
                LogTail: null)
        };

        var rendered = AgentActivityFunctions.RenderCiFindings(findings);

        Assert.Contains("## Check: build-api", rendered);
        Assert.Contains("src/api/Program.cs:12 [failure] CS0103", rendered);
    }

    [Fact]
    public void Findings_use_log_tail_when_no_annotations_and_skip_empty_checks()
    {
        var findings = new List<FailedCheckFinding>
        {
            new("build-frontend", [], "error TS2304: Cannot find name 'Foo'."),
            new("mystery-check", [], LogTail: null), // nothing extractable — skipped
        };

        var rendered = AgentActivityFunctions.RenderCiFindings(findings);

        Assert.Contains("TS2304", rendered);
        Assert.Contains("```", rendered);
        Assert.DoesNotContain("mystery-check", rendered);
    }

    [Fact]
    public void Findings_prefer_log_tail_over_content_free_annotations()
    {
        var findings = new List<FailedCheckFinding>
        {
            new("build-api",
                [new CheckAnnotation(".github", 28, "failure", "Process completed with exit code 1."),
                 new CheckAnnotation(".github", 2, "warning", "Node.js 20 actions are deprecated.")],
                "Api.csproj : error NU1605: Detected package downgrade: Azure.Identity")
        };

        var rendered = AgentActivityFunctions.RenderCiFindings(findings);

        Assert.Contains("NU1605", rendered);
        Assert.DoesNotContain("Process completed", rendered);
    }

    [Theory]
    [InlineData(".github/workflows/deploy.yml", true)]
    [InlineData(".github/CODEOWNERS", true)]
    [InlineData("tests/e2e/specs/auth.spec.ts", true)]       // immutable auth contract spec
    [InlineData("tests/e2e/playwright.config.ts", true)]
    [InlineData("tests/e2e/specs/acceptance.spec.ts", false)] // platform AUTHORS this once on first build
    // Scaffold-first (#131): the immutable app shell is protected …
    [InlineData("src/frontend/src/main.tsx", true)]
    [InlineData("src/frontend/src/app/AppShell.tsx", true)]
    [InlineData("src/frontend/src/lib/api.ts", true)]
    [InlineData("src/frontend/src/vite-env.d.ts", true)]
    [InlineData("src/api/Program.cs", true)]
    [InlineData("src/api/Auth/ClerkJwtMiddleware.cs", true)]
    [InlineData("src/api/Auth/ClerkTokenValidator.cs", true)]
    [InlineData("src/api/Data/CosmosClientFactory.cs", true)]
    [InlineData("src/api/Functions/HealthFunction.cs", true)]
    [InlineData("src/api/host.json", true)]
    [InlineData("src/api/Api.csproj", true)]
    // … but the feature slots and the AI-replaceable sample feature are NOT protected.
    [InlineData("src/frontend/src/app/routes.tsx", false)]
    [InlineData("src/frontend/src/app/nav.ts", false)]
    [InlineData("src/frontend/src/theme.ts", false)]
    [InlineData("src/frontend/src/features/booking/BookingPage.tsx", false)]
    [InlineData("src/api/Features/FeatureRegistration.cs", false)]   // the DI seam the AI writes to
    [InlineData("src/api/Features/Bookings/BookingFunction.cs", false)]
    [InlineData("src/api/Data/CosmosItemStore.cs", false)]           // sample feature — AI-replaceable
    [InlineData("src/api/Functions/ItemsFunction.cs", false)]        // sample feature — AI-replaceable
    [InlineData("src/frontend/src/App.test.tsx", false)]      // the app's own unit tests are fair game
    [InlineData("github-helper/notes.md", false)]
    public void Always_protected_paths_block_authoring_except_acceptance_spec(string path, bool expectProtected)
    {
        Assert.Equal(expectProtected, AgentActivityFunctions.IsProtectedPath(path));
    }

    [Theory]
    // existing has 2 tests / 2 expects / 0 skip / 0 throw
    [InlineData("test('a',()=>{expect(1).toBe(1)}); test('b',()=>{expect(2).toBe(2)})", false)] // identical — fine
    [InlineData("test('a',()=>{expect(1).toBe(1)}); test('b',()=>{expect(2).toBe(2); expect(3).toBe(3)})", false)] // more asserts — fine
    [InlineData("test('a',()=>{expect(1).toBe(1)})", true)]                          // a test removed — gutting
    [InlineData("test('a',()=>{}); test('b',()=>{})", true)]                          // assertions removed — gutting
    [InlineData("test.skip('a',()=>{expect(1).toBe(1)}); test('b',()=>{expect(2).toBe(2)})", true)] // newly skipped — gutting
    [InlineData("test('a',()=>{throw 1}); test('b',()=>{expect(2).toBe(2)})", true)]  // assertion→throw — gutting
    public void Acceptance_spec_regression_is_detected(string proposed, bool expectRegression)
    {
        const string existing = "test('a',()=>{expect(1).toBe(1)}); test('b',()=>{expect(2).toBe(2)})";
        Assert.Equal(expectRegression, AgentActivityFunctions.IsAcceptanceSpecRegression(existing, proposed));
    }

    [Fact]
    public void Acceptance_spec_regression_blocks_when_existing_unknown()
    {
        // Can't verify on a repair → block (preserves the original #115 protection).
        Assert.True(AgentActivityFunctions.IsAcceptanceSpecRegression(null, "test('a',()=>{expect(1).toBe(1)})"));
    }

    [Fact]
    public void Repair_filter_drops_protected_paths_and_gutted_acceptance_spec_but_keeps_maintenance()
    {
        var findings = "src/api/Program.cs:12 CS0103\nApp.tsx:3 TS2304\nacceptance.spec.ts register helper flaky";
        const string existingSpec = "test('a',()=>{expect(1).toBe(1)}); test('b',()=>{expect(2).toBe(2)})";
        var changes = new List<AiSdlc.Shared.FileChange>
        {
            new("src/api/Program.cs", "fixed"),                      // implicated
            new("src/frontend/src/App.tsx", "fixed"),                // implicated
            new("src/api/Namespaces/Renamed.cs", "refactor"),        // NOT implicated — dropped
            new(".github/workflows/deploy.yml", "tampered"),         // protected — dropped
            // a MAINTENANCE edit to acceptance.spec.ts (same tests/asserts, robust helper) — kept
            new("tests/e2e/specs/acceptance.spec.ts",
                "test('a',()=>{expect(1).toBe(1)}); test('b',()=>{expect(2).toBe(2)}) /* robust */"),
        };

        var filtered = AgentActivityFunctions.FilterRepairChanges(changes, findings, existingSpec);

        Assert.DoesNotContain(filtered, f => f.Path.Contains("Renamed"));
        Assert.DoesNotContain(filtered, f => f.Path.StartsWith(".github/"));
        Assert.Contains(filtered, f => f.Path.Contains("acceptance.spec.ts")); // maintenance survives

        // ...but a GUTTING edit (one test deleted) to the same file is dropped.
        var gutting = new List<AiSdlc.Shared.FileChange>
        {
            new("src/api/Program.cs", "fixed"),
            new("tests/e2e/specs/acceptance.spec.ts", "test('a',()=>{expect(1).toBe(1)})"),
        };
        var filtered2 = AgentActivityFunctions.FilterRepairChanges(gutting, findings, existingSpec);
        Assert.DoesNotContain(filtered2, f => f.Path.Contains("acceptance.spec.ts"));
    }

    [Fact]
    public void Repair_filter_falls_back_to_unprotected_set_when_nothing_matches()
    {
        // Findings may reference files indirectly — never brick the repair entirely,
        // but protected paths stay out even in the fallback.
        var changes = new List<AiSdlc.Shared.FileChange>
        {
            new("src/api/Helpers.cs", "fix"),
            new(".github/workflows/ci.yml", "tampered"),
        };

        var filtered = AgentActivityFunctions.FilterRepairChanges(changes, "unrelated findings text");

        var change = Assert.Single(filtered);
        Assert.Equal("src/api/Helpers.cs", change.Path);
    }

    [Fact]
    public void Findings_render_empty_when_nothing_actionable()
    {
        var findings = new List<FailedCheckFinding> { new("x", [], null) };
        Assert.Equal(string.Empty, AgentActivityFunctions.RenderCiFindings(findings));
    }

    [Theory]
    [InlineData(true, false, true, true)]    // reopen findings + source → repair
    [InlineData(false, true, true, true)]    // ci findings + source → repair
    [InlineData(true, true, true, true)]     // both findings + source → repair
    [InlineData(true, false, false, false)]  // findings, no source → not a repair (greenfield)
    [InlineData(false, false, true, false)]  // source only, no findings → not a repair
    [InlineData(false, false, false, false)] // nothing → greenfield
    public void IsRepairRun_requires_findings_and_existing_source(
        bool reopen, bool ci, bool source, bool expect)
    {
        var meta = new Dictionary<string, object>();
        if (reopen) meta["reopenFindings"] = "login page broken";
        if (ci)     meta["ciFindings"]     = "ci-ref";
        if (source) meta["existingSource"] = "source-ref";

        Assert.Equal(expect, AgentActivityFunctions.IsRepairRun(meta));
    }

    [Fact]
    public void IsRepairRun_treats_blank_values_as_absent()
    {
        var meta = new Dictionary<string, object> { ["reopenFindings"] = "  ", ["existingSource"] = "ref" };
        Assert.False(AgentActivityFunctions.IsRepairRun(meta));
    }

    [Fact]
    public void Source_selection_floats_findings_implicated_files_to_the_front()
    {
        // Order is by implication first (stable within each group), then tree order — so the
        // file the findings name survives even when earlier-in-tree files exist.
        var tree = new List<RepoTreeEntry>
        {
            new("src/api/Program.cs", 100),
            new("src/api/Controllers/CoachController.cs", 100),
            new("src/frontend/src/components/LoginPage.tsx", 100),
        };

        var ordered = AgentActivityFunctions.SelectRepairSourcePaths(tree, "auth.spec.ts fails: LoginPage.tsx missing selector");

        Assert.Equal("src/frontend/src/components/LoginPage.tsx", ordered[0]);
        Assert.Equal(3, ordered.Count); // all fit; prioritisation only reorders
    }

    [Fact]
    public void Source_selection_keeps_implicated_file_when_budget_would_otherwise_drop_it()
    {
        // Fill the budget with max-size filler files ahead of the implicated file in tree order.
        // Each file respects the per-file cap, so eligibility never excludes them — only the
        // total budget does. Counts are derived from the constants so the test survives tuning.
        var fileSize    = AgentActivityFunctions.RepairSourceMaxFileBytes;          // == per-file cap (still eligible)
        var fillerCount = AgentActivityFunctions.RepairSourceTotalBudget / fileSize; // exhausts budget to < one more file
        var tree = Enumerable.Range(0, fillerCount)
            .Select(i => new RepoTreeEntry($"src/api/Filler{i}.cs", fileSize))
            .Append(new RepoTreeEntry("src/frontend/src/components/LoginPage.tsx", fileSize))
            .ToList();

        var withFindings = AgentActivityFunctions.SelectRepairSourcePaths(tree, "fix LoginPage.tsx");
        Assert.Contains("src/frontend/src/components/LoginPage.tsx", withFindings);

        // Without findings, tree order wins and the implicated file is squeezed out at the end.
        var withoutFindings = AgentActivityFunctions.SelectRepairSourcePaths(tree);
        Assert.DoesNotContain("src/frontend/src/components/LoginPage.tsx", withoutFindings);
    }

    [Fact]
    public void Findings_total_cap_drops_whole_check_sections_from_the_front()
    {
        var bigTail = new string('x', AgentActivityFunctions.CiFindingsPerCheckMaxChars);
        var findings = Enumerable.Range(0, 5)
            .Select(i => new FailedCheckFinding($"check-{i}", [], bigTail))
            .ToList();

        var rendered = AgentActivityFunctions.RenderCiFindings(findings);

        Assert.True(rendered.Length <= AgentActivityFunctions.CiFindingsMaxChars);
        Assert.Contains("check-4", rendered);       // newest sections survive
        Assert.DoesNotContain("## Check: check-0", rendered); // oldest dropped whole
    }
    // w1proof0: the PO-fix round-trip echoed prompt-redaction masks ([REDACTED:SORT_CODE]) into SVG
    // path data and shipped corrupt graphics. Echoed masks must never be committed on any seam.
    [Fact]
    public void ContainsRedactionEcho_flags_echoed_masks_only()
    {
        Assert.True(AgentActivityFunctions.ContainsRedactionEcho(
            new AiSdlc.Shared.FileChange("favicon.svg", "<path d=\"M10 20 Q [REDACTED:SORT_CODE] 40\"/>")));
        Assert.False(AgentActivityFunctions.ContainsRedactionEcho(
            new AiSdlc.Shared.FileChange("index.html", "<h1>Harbor Lane Florist</h1>")));
    }

    [Fact]
    public void Repair_filter_drops_changes_carrying_redaction_echo()
    {
        var changes = new List<AiSdlc.Shared.FileChange>
        {
            new("index.html", "phone: +44 (0)1584 [REDACTED:SORT_CODE]"),
            new("styles.css", ".hero { color: plum; }"),
        };

        var filtered = AgentActivityFunctions.FilterRepairChanges(changes, "index.html styles.css");

        var kept = Assert.Single(filtered);
        Assert.Equal("styles.css", kept.Path);
    }
}
