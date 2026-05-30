using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Shared.Tests;

public sealed class DomainModelsTests
{
    [Fact]
    public void AgentContext_ShouldStoreRequiredWorkflowInformation()
    {
        var context = new AgentContext
        {
            RunId = "run-001",
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 1,
            CurrentState = WorkflowRunStatus.Analysing.ToString(),
            RequestedAgent = "BusinessAnalyst",
            ArtefactRefs = new Dictionary<string, string>
            {
                ["refinedBrief"] = "artefacts/refined-brief.md"
            }
        };

        Assert.Equal("run-001", context.RunId);
        Assert.Equal("kcsnap/ai-sdlc-platform", context.Repository);
        Assert.Equal(1, context.IssueNumber);
        Assert.Equal("Analysing", context.CurrentState);
        Assert.Equal("BusinessAnalyst", context.RequestedAgent);
        Assert.Equal("artefacts/refined-brief.md", context.ArtefactRefs["refinedBrief"]);
    }

    [Fact]
    public void AgentContext_Mode_DefaultsToStandard()
    {
        var context = new AgentContext
        {
            RunId = "run-001",
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 1,
            CurrentState = WorkflowRunStatus.Started.ToString(),
            RequestedAgent = "ProductStrategist"
        };

        Assert.Equal(WorkflowMode.Standard, context.Mode);
    }

    [Fact]
    public void AgentContext_Mode_CanBeBootstrap()
    {
        var context = new AgentContext
        {
            RunId = "run-001",
            Repository = "kcsnap/user-app-abc12345",
            IssueNumber = 1,
            CurrentState = WorkflowRunStatus.Started.ToString(),
            RequestedAgent = "ProductStrategist",
            Mode = WorkflowMode.Bootstrap
        };

        Assert.Equal(WorkflowMode.Bootstrap, context.Mode);
    }

    [Fact]
    public void AgentResult_ShouldRepresentFollowUpQuestionsAndBlockingIssues()
    {
        var result = new AgentResult
        {
            AgentName = "ProductOwner",
            Status = "NeedsClarification",
            Summary = "More information is required.",
            OutputMarkdown = "## Follow-up needed",
            FollowUpQuestions = new List<string> { "Who is the target user?" },
            BlockingIssues = new List<string> { "Acceptance criteria are missing." }
        };

        Assert.Equal("ProductOwner", result.AgentName);
        Assert.Equal("NeedsClarification", result.Status);
        Assert.Equal("## Follow-up needed", result.OutputMarkdown);
        Assert.Single(result.FollowUpQuestions);
        Assert.Single(result.BlockingIssues);
    }

    [Fact]
    public void WorkflowRun_ShouldDefaultRiskValuesToUnknown()
    {
        var run = new WorkflowRun
        {
            RunId = "run-001",
            Repository = "kcsnap/ai-sdlc-platform",
            Issue = new GitHubIssueReference("kcsnap/ai-sdlc-platform", 1, "https://github.com/kcsnap/ai-sdlc-platform/issues/1"),
            Status = WorkflowRunStatus.Started,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        Assert.Equal(RiskLevel.Unknown, run.RiskLevel);
        Assert.Equal("Unknown", run.RiskDecision);
    }

    [Fact]
    public void AuditEvent_ShouldCaptureDecisionAndRiskLevel()
    {
        var auditEvent = new AuditEvent
        {
            RunId = "run-001",
            TimestampUtc = DateTimeOffset.UtcNow,
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 1,
            ActorType = "Agent",
            ActorName = "RiskAssessor",
            Action = "RiskAssessmentCompleted",
            Summary = "Change is eligible for autonomous deployment.",
            Decision = RiskDecision.ContinueAutonomously.ToString(),
            RiskLevel = RiskLevel.Low.ToString(),
            RedactionApplied = true
        };

        Assert.Equal("RiskAssessor", auditEvent.ActorName);
        Assert.Equal("RiskAssessmentCompleted", auditEvent.Action);
        Assert.Equal("Low", auditEvent.RiskLevel);
        Assert.True(auditEvent.RedactionApplied);
    }
}
