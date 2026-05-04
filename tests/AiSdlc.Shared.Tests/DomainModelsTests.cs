using AiSdlc.Shared;

namespace AiSdlc.Shared.Tests;

public sealed class DomainModelsTests
{
    [Fact]
    public void AgentContext_ShouldStoreRequiredWorkflowInformation()
    {
        var artefact = new ArtefactReference("brief", "markdown", "artefacts/refined-brief.md");

        var context = new AgentContext
        {
            RunId = "run-001",
            Repository = "kcsnap/ai-sdlc-platform",
            IssueNumber = 1,
            CurrentState = WorkflowRunStatus.Analysing,
            RequestedAgent = "BusinessAnalyst",
            Artefacts = new Dictionary<string, ArtefactReference>
            {
                ["refinedBrief"] = artefact
            }
        };

        Assert.Equal("run-001", context.RunId);
        Assert.Equal("kcsnap/ai-sdlc-platform", context.Repository);
        Assert.Equal(1, context.IssueNumber);
        Assert.Equal(WorkflowRunStatus.Analysing, context.CurrentState);
        Assert.Equal("BusinessAnalyst", context.RequestedAgent);
        Assert.Same(artefact, context.Artefacts["refinedBrief"]);
    }

    [Fact]
    public void AgentResult_ShouldRepresentFollowUpQuestionsAndBlockingIssues()
    {
        var result = new AgentResult
        {
            AgentName = "ProductOwner",
            Status = "NeedsClarification",
            Summary = "More information is required.",
            FollowUpQuestions = new[] { "Who is the target user?" },
            BlockingIssues = new[] { "Acceptance criteria are missing." }
        };

        Assert.Equal("ProductOwner", result.AgentName);
        Assert.Equal("NeedsClarification", result.Status);
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
        Assert.Equal(RiskDecision.Unknown, run.RiskDecision);
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
            RiskLevel = AiSdlc.Shared.RiskLevel.Low,
            RedactionApplied = true
        };

        Assert.Equal("RiskAssessor", auditEvent.ActorName);
        Assert.Equal("RiskAssessmentCompleted", auditEvent.Action);
        Assert.Equal(AiSdlc.Shared.RiskLevel.Low, auditEvent.RiskLevel);
        Assert.True(auditEvent.RedactionApplied);
    }
}
