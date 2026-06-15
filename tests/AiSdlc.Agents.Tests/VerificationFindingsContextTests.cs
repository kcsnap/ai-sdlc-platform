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

    [Fact]
    public void Repair_mode_doc_is_added_only_with_findings_plus_existing_source()
    {
        // findings + existing source → targeted-repair instruction pins the pipeline to findings
        var repair = MakeContext();
        repair.Metadata["reopenFindings"] = "AuthGate.tsx uses RedirectToSignIn";
        repair.Metadata["existingSource"] = "<file path=\"AuthGate.tsx\">...</file>";
        var repairDocs = new Dictionary<string, string>();
        AgentContextDocuments.AddStandard(repairDocs, repair);
        Assert.True(repairDocs.ContainsKey(AgentContextDocuments.RepairModeDocumentName));
        Assert.Contains("TARGETED REPAIR", repairDocs[AgentContextDocuments.RepairModeDocumentName]);

        // findings but no source (fresh build with stale findings) → no repair-mode doc
        var noSource = MakeContext();
        noSource.Metadata["reopenFindings"] = "something";
        var noSourceDocs = new Dictionary<string, string>();
        AgentContextDocuments.AddStandard(noSourceDocs, noSource);
        Assert.False(noSourceDocs.ContainsKey(AgentContextDocuments.RepairModeDocumentName));

        // plain fresh build → no repair-mode doc
        var freshDocs = new Dictionary<string, string>();
        AgentContextDocuments.AddStandard(freshDocs, MakeContext());
        Assert.False(freshDocs.ContainsKey(AgentContextDocuments.RepairModeDocumentName));
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
