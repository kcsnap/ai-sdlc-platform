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
        Assert.False(docs.ContainsKey(AgentContextDocuments.CiFindingsDocumentName));
    }

    [Fact]
    public void Ci_findings_produce_the_ci_document_with_its_preamble()
    {
        var context = MakeContext();
        context.Metadata["ciFindings"] = "src/App.tsx:3 [failure] TS2304";

        var docs = new Dictionary<string, string>();
        AgentContextDocuments.AddStandard(docs, context);

        var doc = docs[AgentContextDocuments.CiFindingsDocumentName];
        Assert.Contains("TS2304", doc);
        Assert.Contains("CI build FAILED", doc);
    }

    [Fact]
    public void Fresh_ci_findings_take_precedence_over_stale_reopen_findings()
    {
        // A reopened run whose repair then fails CI: the reopen findings describe defects
        // that were already fixed — only the current compiler output may reach the agents.
        var context = MakeContext();
        context.Metadata["reopenFindings"] = "old verification findings";
        context.Metadata["ciFindings"]     = "fresh compiler output";

        var docs = new Dictionary<string, string>();
        AgentContextDocuments.AddStandard(docs, context);

        Assert.True(docs.ContainsKey(AgentContextDocuments.CiFindingsDocumentName));
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
