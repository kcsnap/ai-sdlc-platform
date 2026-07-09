using AiSdlc.Orchestrator.Functions;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

/// <summary>
/// Q1(b): generated-acceptance-test content lint — the known-bad patterns that shipped broken suites
/// (invented Playwright API; wrong form-relay endpoint shape). A violating spec is rejected at both the
/// first-build and repair sanitize seams so the fix loop re-authors it.
/// </summary>
public sealed class AcceptanceSpecLintTests
{
    private const string SpecPath = "tests/e2e/specs/acceptance.spec.ts";

    [Fact]
    public void Flags_toHaveJSProperty()
    {
        var violations = AgentActivityFunctions.AcceptanceSpecLintViolations(
            "await expect(img).toHaveJSProperty('naturalWidth', (w) => w > 0);");
        Assert.Single(violations);
        Assert.Contains("toHaveJSProperty", violations[0]);
    }

    [Fact]
    public void Flags_per_form_endpoint()
    {
        var violations = AgentActivityFunctions.AcceptanceSpecLintViolations(
            "await page.route('/api/forms/contact-form/submit', r => r.fulfill());");
        Assert.Single(violations);
        Assert.Contains("/api/forms/submit", violations[0]);
    }

    [Fact]
    public void Clean_spec_passes_including_the_flat_endpoint()
    {
        var violations = AgentActivityFunctions.AcceptanceSpecLintViolations(
            "await expect(page.getByTestId('app-ready')).toBeVisible(); // posts to /api/forms/submit");
        Assert.Empty(violations);
    }

    [Fact]
    public void First_build_screen_rejects_a_violating_spec_but_keeps_other_files()
    {
        var violating = new FileChange(SpecPath, "expect(x).toHaveJSProperty('a', cb)");
        var clean     = new FileChange("index.html", "<html>ok</html>");

        Assert.True(AgentActivityFunctions.IsRejectedAcceptanceSpec(violating, existingAcceptanceSpec: null, isRepair: false));
        Assert.False(AgentActivityFunctions.IsRejectedAcceptanceSpec(clean, existingAcceptanceSpec: null, isRepair: false));
    }

    [Fact]
    public void Repair_filter_drops_a_violating_spec()
    {
        var existing = "test('a', () => {}); expect(1).toBe(1);";
        var changes = new[]
        {
            new FileChange(SpecPath, existing + "\nexpect(img).toHaveJSProperty('naturalWidth', w => w > 0);"),
            new FileChange("app.js", "console.log('fix');")
        };

        var filtered = AgentActivityFunctions.FilterRepairChanges(changes, "fix app.js", existing);

        Assert.DoesNotContain(filtered, c => c.Path == SpecPath);
        Assert.Contains(filtered, c => c.Path == "app.js");
    }
}
