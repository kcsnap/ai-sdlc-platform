using AiSdlc.GitHub;
using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class CiRepairLoopTests
{
    [Theory]
    [InlineData(1, 0, 0, true)]   // concrete failure, attempts available
    [InlineData(1, 0, 2, false)]  // attempts exhausted
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
    [InlineData("src/api/Program.cs", false)]
    [InlineData("github-helper/notes.md", false)]
    public void Github_directory_is_protected(string path, bool expectProtected)
    {
        Assert.Equal(expectProtected, AgentActivityFunctions.IsProtectedPath(path));
    }

    [Fact]
    public void Repair_filter_keeps_only_findings_implicated_files_and_drops_protected_paths()
    {
        var findings = "src/api/Program.cs:12 [failure] CS0103\nApp.tsx:3 [failure] TS2304";
        var changes = new List<AiSdlc.Shared.FileChange>
        {
            new("src/api/Program.cs", "fixed"),                      // implicated by full path
            new("src/frontend/src/App.tsx", "fixed"),                // implicated by filename
            new("src/api/Namespaces/Renamed.cs", "refactor"),        // NOT implicated — dropped
            new(".github/workflows/deploy.yml", "tampered"),         // protected — dropped
        };

        var filtered = AgentActivityFunctions.FilterRepairChanges(changes, findings);

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, f => f.Path.Contains("Renamed"));
        Assert.DoesNotContain(filtered, f => f.Path.StartsWith(".github/"));
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
}
