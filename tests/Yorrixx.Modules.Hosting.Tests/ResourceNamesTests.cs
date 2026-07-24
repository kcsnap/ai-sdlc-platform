using Xunit;
using Yorrixx.Modules.Hosting.Internal;

namespace Yorrixx.Modules.Hosting.Tests;

public class ResourceNamesTests
{
    [Fact]
    public void From_DerivesSlug8AndId8()
    {
        var names = ResourceNames.From("3e14295b-aaaa-bbbb-cccc-ddddeeeeffff", "Ramp W3 Bonanza");

        Assert.Equal("rampw3bo", names.Slug8);
        Assert.Equal("3e14295b", names.Id8);
        Assert.Equal("appi-rampw3bo-3e14295b", names.AppInsights);
    }

    [Fact]
    public void FailureAnomaliesAlertRule_MatchesAzureAutoCreatedRuleName()
    {
        // Azure names the auto-created smart-detector rule
        // "Failure Anomalies - {componentName}" — deprovision relies on this
        // to find and delete the rule the platform never created itself.
        var names = ResourceNames.From("3e14295b-aaaa-bbbb-cccc-ddddeeeeffff", "Ramp W3 Bonanza");

        Assert.Equal("Failure Anomalies - appi-rampw3bo-3e14295b", names.FailureAnomaliesAlertRule);
    }

    [Fact]
    public void From_EmptyAppName_FallsBackToAppSlug()
    {
        var names = ResourceNames.From("3e14295b-aaaa-bbbb-cccc-ddddeeeeffff", string.Empty);

        Assert.Equal("app", names.Slug8);
        Assert.Equal("Failure Anomalies - appi-app-3e14295b", names.FailureAnomaliesAlertRule);
    }
}
