using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Agents.Tests;

public sealed class BusinessAnalystAgentTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldGenerateDeveloperReadyMarkdownWhenSpecIsComplete()
    {
        var agent = new BusinessAnalystAgent();
        var request = new AgentExecutionRequest
        {
            AgentName = AgentNames.BusinessAnalyst,
            Context = CreateContext(
                specTitle: "Add delivery information section",
                specMarkdown:
"""
## What do you want to create or change?

Add a delivery information section to the product detail page.

## Why is this needed?

Customers need delivery expectations before making a purchase enquiry.

## Who is the user or customer?

Prospective buyers comparing products.

## Is this for a new app or an existing app?

- [x] Existing app

## Any known constraints?

- Use the existing product detail layout.
- Do not change checkout behaviour.

## Any examples, screenshots, links, or reference material?

Reference the current shipping summary used on featured products.

## Definition of done, if known

The delivery section appears on all product detail pages and is covered by updated tests.
""",
                existingProductContext: "The current product detail page already shows pricing, imagery, and an enquiry CTA but no delivery expectations.")
        };

        var result = await agent.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.OutputMarkdown);
        Assert.Contains("## Developer Handoff", result.OutputMarkdown);
        Assert.Contains("delivery information section", result.OutputMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.BlockingIssues);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRequestClarificationWhenCriticalSectionsAreMissing()
    {
        var agent = new BusinessAnalystAgent();
        var request = new AgentExecutionRequest
        {
            AgentName = AgentNames.BusinessAnalyst,
            Context = CreateContext(
                specTitle: "Incomplete request",
                specMarkdown:
"""
## What do you want to create or change?

Update the page.
""",
                existingProductContext: string.Empty)
        };

        var result = await agent.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("NeedsClarification", result.Status);
        Assert.NotEmpty(result.BlockingIssues);
        Assert.Contains(result.FollowUpQuestions, item => item.Contains("primary user", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("## Follow-up Questions", result.OutputMarkdown);
    }

    [Fact]
    public async Task AgentRunner_ShouldExecuteBusinessAnalystByName()
    {
        IAgentRunner runner = new AgentRunner(new IAgent[] { new BusinessAnalystAgent() });
        var request = new AgentExecutionRequest
        {
            AgentName = AgentNames.BusinessAnalyst,
            Context = CreateContext(
                specTitle: "Spec",
                specMarkdown:
"""
## What do you want to create or change?

Add a new support link.

## Why is this needed?

Users cannot find support.

## Who is the user or customer?

Existing customers.
""",
                existingProductContext: "The current footer has legal and social links only.")
        };

        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Result);
        Assert.Equal(AgentNames.BusinessAnalyst, result.Result.AgentName);
    }

    private static AgentContext CreateContext(string specTitle, string specMarkdown, string existingProductContext) =>
        new()
        {
            RunId = "run-001",
            Repository = "example/repo",
            IssueNumber = 101,
            CurrentState = "Analysing",
            RequestedAgent = AgentNames.BusinessAnalyst,
            Metadata = new Dictionary<string, object>
            {
                ["specTitle"] = specTitle,
                ["specMarkdown"] = specMarkdown,
                ["existingProductContext"] = existingProductContext
            }
        };
}
