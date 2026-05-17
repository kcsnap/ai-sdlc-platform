namespace AiSdlc.Dashboard.Services;

public sealed record WorkflowStageDefinition(string Label, IReadOnlyList<string> AgentNames);

// Hard-coded view of the orchestrator's workflow stages. Mirrors AiSdlcWorkflowOrchestrator.cs.
// Agent names use the exact strings from AiSdlc.Agents.AgentNames so they match audit ActorName.
public static class WorkflowDefinition
{
    // Synthetic terminal node — not a real agent, driven by RunStatus.Released.
    public const string MergedNodeName = "Merged";

    public static readonly IReadOnlyList<WorkflowStageDefinition> Stages = new WorkflowStageDefinition[]
    {
        new("Strategy",       new[] { "Product Strategist" }),
        new("Brief",          new[] { "Product Owner" }),
        new("Analysis",       new[] { "Business Analyst" }),
        new("Architecture",   new[] { "Architect" }),
        new("Specialist Reviews", new[]
        {
            "Security & Privacy Reviewer",
            "UX / Accessibility Reviewer",
            "DevOps / Platform Engineer",
            "Content / SEO Reviewer",
            "Compliance / Legal Reviewer",
            "Data / Analytics Reviewer"
        }),
        new("Implementation Plan", new[]
        {
            "QA / Test Engineer",
            "Senior Coder"
        }),
        new("Risk",           new[] { "Risk Assessor" }),
        new("Release",        new[] { "Release Manager" }),
        new("Implementation", new[] { "Code Implementer" }),
        new("Branch Review",  new[] { "Product Owner Branch Reviewer" }),
        new("Merge",          new[] { MergedNodeName })
    };
}
