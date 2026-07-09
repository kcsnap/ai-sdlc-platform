using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AiSdlc.Contracts.Callbacks;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.Orchestrator.Webhooks;
// The Builds namespace has its own (internal) VerificationCheck; the wire payload uses the CONTRACT one.
using ContractVerificationCheck = AiSdlc.Contracts.Callbacks.VerificationCheck;
using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Yorrixx.Provisioner.Contracts;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// New-path build orchestrator (Phase 1) — entry point for API-initiated builds, where the Charter
/// arrives via create-build and no repo exists yet. Pipeline: derive profile (deterministic stackProfile)
/// → create repo from template (GitHub App) → Call 1 /provision + Call-2 result (poll fallback) → write
/// repo deploy vars → verification gate → drive /status, /runtime, /verification callbacks to Yorrixx
/// (/runtime before /status:live so the publish email carries the hosted URL).
/// </summary>
public static class NewAppBuildOrchestrator
{
    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DeployPollInterval = TimeSpan.FromSeconds(30);
    private const int MaxDeployPolls = 40;   // ~20 min for the deploy workflow to finish

    // F1: how long a build may sit at ready-for-review awaiting the owner's signoff before failing.
    private static readonly TimeSpan ReviewApprovalTimeout = TimeSpan.FromDays(7);

    // Charter file wire shape (.yorrixx/charter.json): PascalCase + STRING enums — the exact shape
    // GitHubCharterReader parses (pinned by CharterParseTests).
    private static readonly JsonSerializerOptions CharterJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Wire options for every Yorrixx callback (camelCase, nulls omitted). Internal so the golden wire-shape
    // tests serialize with EXACTLY the sender's options (A11: named records must stay byte-identical).
    internal static readonly JsonSerializerOptions CallbackJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Function(nameof(NewAppBuildOrchestrator))]
    public static async Task<string> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<CreateBuildRequest>()
            ?? throw new InvalidOperationException("Build input must include a CreateBuildRequest payload.");
        var charter = request.Charter
            ?? throw new InvalidOperationException("Build input must include a Charter.");

        // Callbacks to Yorrixx. Delivery failures are retried inside the activity, then recorded here and
        // surfaced via custom status + the run output (G6 P4) — a dead callback never fails the build, but
        // it can never look green either.
        var callbackFailures = 0;
        async Task Emit(string kind, object payload)
        {
            try
            {
                await context.CallActivityAsync(
                    nameof(BuildActivityFunctions.SendCallbackAsync),
                    new CallbackMessage(request.CallbackBaseUrl, request.AppId, kind, JsonSerializer.Serialize(payload, CallbackJson)));
            }
            catch (TaskFailedException)
            {
                callbackFailures++;
                context.SetCustomStatus(new { callbackFailures });
            }
        }
        Task Status(string status, string? phase = null, string? detail = null) =>
            Emit("status", new StatusCallback(status, phase, detail));

        await Status(CanonicalBuildStatus.Queued);

        // Component 2 — deterministic profile gate (no LLM): Static iff no backend need; else FullStack.
        var stackProfile = StackProfiles.Resolve(charter);

        // Component 3 — create the user-app repo from the stack-appropriate template (GitHub App).
        var repo = await context.CallActivityAsync<CreatedRepository>(
            nameof(BuildActivityFunctions.CreateUserAppRepoAsync),
            new CreateRepoInput(request.AppId, stackProfile.ToString()));

        // Component 4 — provision via the dedicated provisioner. Call 1 is async; the Call-2 callback raises
        // 'provision-result', and a GET poll is the fallback if it's dropped.
        await Status(CanonicalBuildStatus.Provisioning, "Provision");
        var (repoOwner, repoName) = SplitFullName(repo.FullName);
        var provisionSpec = new ProvisionSpec(
            AppId:        request.AppId,
            BuildId:      context.InstanceId,
            Env:          "dev",
            Region:       "northeurope",
            StackProfile: stackProfile.ToString(),
            Capabilities: CapabilityResolver.Resolve(charter, databaseDerived: charter.Constraints.NeedsPersistence)
                              .ToProvisionCapabilities(),
            Repo:         new ProvisionRepo(repoOwner, repoName, repo.DefaultBranch),
            // OwnerRef is the owner's Clerk user id (opaque to the platform) — the provisioner needs it as
            // created_by on the Clerk org. AppName feeds the resource slug + org display name.
            OwnerUserId:  string.IsNullOrWhiteSpace(request.OwnerRef) ? null : request.OwnerRef,
            AppName:      string.IsNullOrWhiteSpace(charter.Identity.AppName) ? null : charter.Identity.AppName);
        await context.CallActivityAsync(nameof(BuildActivityFunctions.StartProvisionAsync), provisionSpec);

        ProvisionResult? result;
        using (var cts = new CancellationTokenSource())
        {
            var resultTask  = context.WaitForExternalEvent<ProvisionResult>(ProvisionResultFunction.EventName, cts.Token);
            var timeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(ProvisionTimeout), cts.Token);
            var winner      = await Task.WhenAny(resultTask, timeoutTask);
            cts.Cancel();
            result = winner == resultTask
                ? await resultTask
                : await context.CallActivityAsync<ProvisionResult?>(
                      nameof(BuildActivityFunctions.PollProvisionResultAsync), context.InstanceId);
        }

        if (result is null || !string.Equals(result.Outcome, "provisioned", StringComparison.OrdinalIgnoreCase))
        {
            var detail = result?.Detail ?? "no provision result before timeout";
            await Status(CanonicalBuildStatus.Failed, "Provision", detail);
            throw new InvalidOperationException($"Provisioning failed for {request.AppId}: {detail}.");
        }

        // Component 4b — commit the provisioner's canonical deploy workflow verbatim (we don't render it;
        // the OIDC triple / resource names / clerk key are already baked into deployYaml).
        if (!string.IsNullOrWhiteSpace(result.DeployYaml))
            await context.CallActivityAsync(
                nameof(BuildActivityFunctions.CommitDeployWorkflowAsync),
                new CommitDeployInput(repo.FullName, result.DeployYaml, repo.DefaultBranch));

        // /runtime BEFORE any 'live' — so the publish email carries the hosted URL. RepoUrl is part of the
        // named contract record (F2): it has been on this wire since the emit was introduced.
        await Emit("runtime", new RuntimeCallback(repo.HtmlUrl, result.HostedUrl));
        await Status(CanonicalBuildStatus.Building, "Build");

        // Component 5 — THE AGENT BUILD (F3). This stage was silently absent: the platform creates the repo
        // on this path, so nothing seeded the charter file or the ai-sdlc:bootstrap issue the agent pipeline
        // is driven by — flip-#4 shipped raw template scaffold as "live". The platform now seeds both and
        // runs the agent pipeline as a SUB-ORCHESTRATION on the issue-derived instance id (comment commands
        // keep routing to it; the reconciliation sweep sees an active instance and stays out; bootstrap mode
        // needs no webhook — risk gates auto-override and merges are orchestrator-driven).
        await context.CallActivityAsync(
            nameof(AgentActivityFunctions.CommitFileAsync),
            new CommitFileInput(repo.FullName, ".yorrixx/charter.json",
                JsonSerializer.Serialize(charter, CharterJson),
                "chore: seed charter for the agent build", repo.DefaultBranch));

        var issueTitle = $"Bootstrap build: {charter.Identity.AppName}";
        var issueBody  = CharterMarkdownRenderer.Render(charter);
        var issue = await context.CallActivityAsync<GitHubIssueReference>(
            nameof(BuildActivityFunctions.SeedBootstrapIssueAsync),
            new SeedBootstrapIssueInput(repo.FullName, issueTitle, issueBody));

        var subInstanceId = GitHubWebhookProcessor.BuildInstanceId(repo.FullName, issue.IssueNumber);
        var agentContext = GitHubWebhookProcessor.BuildAgentContext(
            subInstanceId, repo.FullName, issue.IssueNumber, WorkflowMode.Bootstrap,
            issueTitle, issueBody, issue.Url, "yorrixx-platform");

        try
        {
            await context.CallSubOrchestratorAsync(
                nameof(AiSdlcWorkflowOrchestrator), agentContext,
                new SubOrchestrationOptions { InstanceId = subInstanceId });
        }
        catch (TaskFailedException ex)
        {
            await Status(CanonicalBuildStatus.Failed, "Build", $"agent build failed: {ex.Message}");
            throw new InvalidOperationException($"Agent build failed for {request.AppId}: {ex.Message}", ex);
        }

        // Component 6 — verification gate: wait for the (post-content-merge) deploy workflow, then probe.
        await Status(CanonicalBuildStatus.Verifying, "Verify");
        var deployStatus = "none";
        for (var poll = 0; poll < MaxDeployPolls; poll++)
        {
            deployStatus = await context.CallActivityAsync<string>(
                nameof(BuildActivityFunctions.GetDeployStatusAsync),
                new DeployStatusInput(repo.FullName, repo.DefaultBranch));
            if (deployStatus is not ("running" or "none"))
                break;
            await context.CreateTimer(context.CurrentUtcDateTime.Add(DeployPollInterval), CancellationToken.None);
        }

        // F3(b) + Q1(c): probe AND read the page. Retry past stale-CDN/deploy-propagation windows until the
        // content stops looking like scaffold; if it never does, the content-not-scaffold check fails the run.
        var servesStatus = 0;
        var pageHtml = string.Empty;
        if (!string.IsNullOrWhiteSpace(result.HostedUrl))
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                servesStatus = await context.CallActivityAsync<int>(nameof(BuildActivityFunctions.ProbeUrlAsync), result.HostedUrl);
                pageHtml = await context.CallActivityAsync<string>(nameof(BuildActivityFunctions.FetchPageAsync), result.HostedUrl);
                if (!BuildActivityFunctions.ContentLooksScaffold(pageHtml, charter.Identity.AppName))
                    break;
                await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(15), CancellationToken.None);
            }
        }

        var verification = BuildActivityFunctions.AssembleVerification(
            deployStatus, servesStatus, stackProfile == StackProfile.Static, pageHtml, charter.Identity.AppName);

        var at = context.CurrentUtcDateTime.ToString("o");
        await Emit("verification", new VerificationCallback(
            verification.Outcome,
            Attempt: 1,
            verification.Checks.Select(c => new ContractVerificationCheck(c.CheckId, c.Name, c.Status, c.Evidence, at)).ToArray()));

        if (!string.Equals(verification.Outcome, "passed", StringComparison.OrdinalIgnoreCase))
        {
            await Status(CanonicalBuildStatus.Failed, "Verify", "verification did not pass");
            return $"failed:{request.AppId}{CallbackSuffix(callbackFailures)}";
        }

        await Status(CanonicalBuildStatus.ReadyForReview, "Review");

        // F1 — going LIVE requires the owner's signoff, relayed by yorrixx-app to
        // POST /api/builds/{appId}/approve (or /request-changes). AutoApproveReview=true restores the old
        // auto-publish as an explicit dev convenience; the code default is the gate ON.
        var autoApprove = await context.CallActivityAsync<bool>(
            nameof(BuildActivityFunctions.GetReviewAutoApproveAsync), (object?)null);
        if (!autoApprove)
        {
            ApprovalSignal? signal;
            using (var cts = new CancellationTokenSource())
            {
                var approvalTask = context.WaitForExternalEvent<ApprovalSignal>(ApproveBuildFunction.EventName, cts.Token);
                var timeoutTask  = context.CreateTimer(context.CurrentUtcDateTime.Add(ReviewApprovalTimeout), cts.Token);
                var winner       = await Task.WhenAny(approvalTask, timeoutTask);
                cts.Cancel();
                signal = winner == approvalTask ? await approvalTask : null;
            }

            if (signal is null)
            {
                await Status(CanonicalBuildStatus.Failed, "Review", "owner signoff not received before timeout");
                return $"failed:{request.AppId}{CallbackSuffix(callbackFailures)}";
            }
            if (!signal.Approved)
            {
                await Status(CanonicalBuildStatus.Failed, "Review", signal.Detail ?? "owner requested changes");
                return $"failed:{request.AppId}{CallbackSuffix(callbackFailures)}";
            }
        }

        await Status(CanonicalBuildStatus.Live, "Live");
        return $"live:{request.AppId}:{result.HostedUrl}{CallbackSuffix(callbackFailures)}";
    }

    private static (string Owner, string Name) SplitFullName(string fullName)
    {
        var i = fullName.IndexOf('/');
        return i > 0 ? (fullName[..i], fullName[(i + 1)..]) : (string.Empty, fullName);
    }

    // Non-empty only when callbacks were lost — makes a delivery-degraded run visible in its output (G6 P4).
    internal static string CallbackSuffix(int callbackFailures) =>
        callbackFailures > 0 ? $":callbacksFailed={callbackFailures}" : string.Empty;
}
