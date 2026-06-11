using AiSdlc.Agents;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

public sealed class VerificationFindingsContextTests
{
    [Fact]
    public void Reopen_findings_reach_every_agent_as_a_context_document()
    {
        var context = MakeContext();
        context.Metadata["reopenFindings"] = "frontend fails to compile: TS2304 in App.tsx";

        var docs = new Dictionary<string, string>();
        AgentContextDocuments.AddStandard(docs, context);

        var doc = docs[AgentContextDocuments.VerificationFindingsDocumentName];
        Assert.Contains("TS2304", doc);
        Assert.Contains("REOPENED", doc); // the preamble tells agents these take priority
    }

    [Fact]
    public void No_findings_means_no_document()
    {
        var docs = new Dictionary<string, string>();
        AgentContextDocuments.AddStandard(docs, MakeContext());
        Assert.False(docs.ContainsKey(AgentContextDocuments.VerificationFindingsDocumentName));
    }

    private static AgentContext MakeContext() => new()
    {
        RunId          = "run-1",
        Repository     = "yorrixx-apps/user-app-test",
        IssueNumber    = 1,
        CurrentState   = "Started",
        RequestedAgent = AgentNames.CodeImplementer
    };
}
