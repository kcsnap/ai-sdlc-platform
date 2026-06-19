using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class StackProfileTests
{
    // Derive-once-stamp: the platform reads Yorrixx's stamped .yorrixx/profile.json. Only an explicit
    // stackProfile == "Static" applies; everything else (absent / wrong key / malformed / FullStack)
    // defaults to "FullStack" — today's behaviour, so the read is inert until Static is stamped.
    [Theory]
    [InlineData("{\"stackProfile\":\"Static\"}", "Static")]
    [InlineData("{ \"stackProfile\": \"static\" }", "Static")]     // value case-insensitive
    [InlineData("{ \"StackProfile\": \"Static\" }", "Static")]     // key case-insensitive
    [InlineData("{\"stackProfile\":\"FullStack\"}", "FullStack")]
    [InlineData("{\"other\":\"Static\"}", "FullStack")]            // wrong key
    [InlineData("{\"stackProfile\":123}", "FullStack")]            // non-string value
    [InlineData("", "FullStack")]                                  // empty
    [InlineData(null, "FullStack")]                                // absent
    [InlineData("not json", "FullStack")]                         // malformed
    [InlineData("[\"Static\"]", "FullStack")]                     // not an object
    public void ParseStackProfile_returns_Static_only_for_an_explicit_static_stamp(string? json, string expected)
    {
        Assert.Equal(expected, AgentActivityFunctions.ParseStackProfile(json));
    }
}
