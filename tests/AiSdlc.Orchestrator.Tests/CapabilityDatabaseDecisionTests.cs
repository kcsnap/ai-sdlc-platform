using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class CapabilityDatabaseDecisionTests
{
    // The Balanced database-need judgment parses {"database": bool} from the model. Any ambiguity or
    // malformed output defaults to TRUE — the safe answer that never silently drops a datastore the app
    // might need. Only an explicit false yields api-only.
    [Theory]
    [InlineData("{\"database\":false,\"rationale\":\"stateless converter\"}", false)]
    [InlineData("{ \"database\": false }", false)]
    [InlineData("Sure! {\"database\": false} done.", false)]            // surrounded by prose
    [InlineData("{\"database\":true}", true)]
    [InlineData("{\"database\":\"false\"}", true)]                       // string, not bool → safe default
    [InlineData("{\"other\":false}", true)]                             // wrong key
    [InlineData("", true)]                                              // empty
    [InlineData(null, true)]                                            // absent
    [InlineData("not json", true)]                                      // malformed
    [InlineData("[false]", true)]                                       // not an object
    public void ParseDatabaseDecision_defaults_to_true_unless_explicitly_false(string? responseText, bool expected)
    {
        Assert.Equal(expected, AgentActivityFunctions.ParseDatabaseDecision(responseText));
    }
}
