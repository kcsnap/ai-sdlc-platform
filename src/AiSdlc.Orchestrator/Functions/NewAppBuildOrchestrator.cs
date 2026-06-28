using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.RepoIndex.Charter;
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

    private static readonly JsonSerializerOptions CallbackJson = new()
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

        // Fire-and-forget callbacks to Yorrixx (the activity swallows transport errors).
        Task Emit(string kind, object payload) => context.CallActivityAsync(
            nameof(BuildActivityFunctions.SendCallbackAsync),
            new CallbackMessage(request.CallbackBaseUrl, request.AppId, kind, JsonSerializer.Serialize(payload, CallbackJson)));
        Task Status(string status, string? phase = null, string? detail = null) =>
            Emit("status", new { status, phase, detail });

        await Status("queued");

        // Component 2 — deterministic profile gate (no LLM): Static iff no backend need; else FullStack.
        var stackProfile = StackProfileResolver.Resolve(charter);

        // Component 3 — create the user-app repo from the stack-appropriate template (GitHub App).
        var repo = await context.CallActivityAsync<CreatedRepository>(
            nameof(BuildActivityFunctions.CreateUserAppRepoAsync),
            new CreateRepoInput(request.AppId, stackProfile.ToString()));

        // Component 4 — provision via the dedicated provisioner. Call 1 is async; the Call-2 callback raises
        // 'provision-result', and a GET poll is the fallback if it's dropped.
        await Status("provisioning", "Provision");
        var (repoOwner, repoName) = SplitFullName(repo.FullName);
        var provisionSpec = new ProvisionSpec(
            AppId:        request.AppId,
            BuildId:      context.InstanceId,
            Env:          "dev",
            Region:       "northeurope",
            StackProfile: stackProfile.ToString(),
            Capabilities: CapabilityResolver.Resolve(charter, databaseDerived: charter.Constraints.NeedsPersistence)
                              .ToProvisionCapabilities(),
            Repo:         new ProvisionRepo(repoOwner, repoName, repo.DefaultBranch));
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
            await Status("failed", "Provision", detail);
            throw new InvalidOperationException($"Provisioning failed for {request.AppId}: {detail}.");
        }

        // Component 4b — commit the provisioner's canonical deploy workflow verbatim (we don't render it;
        // the OIDC triple / resource names / clerk key are already baked into deployYaml).
        if (!string.IsNullOrWhiteSpace(result.DeployYaml))
            await context.CallActivityAsync(
                nameof(BuildActivityFunctions.CommitDeployWorkflowAsync),
                new CommitDeployInput(repo.FullName, result.DeployYaml, repo.DefaultBranch));

        // /runtime BEFORE any 'live' — so the publish email carries the hosted URL.
        await Emit("runtime", new { repoUrl = repo.HtmlUrl, hostedUrl = result.HostedUrl });
        await Status("building", "Build");

        // Component 5 — verification gate: wait for the template's deploy workflow, then probe the hosted URL.
        await Status("verifying", "Verify");
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

        var servesStatus = string.IsNullOrWhiteSpace(result.HostedUrl)
            ? 0
            : await context.CallActivityAsync<int>(nameof(BuildActivityFunctions.ProbeUrlAsync), result.HostedUrl);

        var verification = BuildActivityFunctions.AssembleVerification(
            deployStatus, servesStatus, stackProfile == StackProfile.Static);

        var at = context.CurrentUtcDateTime.ToString("o");
        await Emit("verification", new
        {
            outcome = verification.Outcome,
            attempt = 1,
            checks  = verification.Checks.Select(c => new { c.CheckId, c.Name, c.Status, c.Evidence, at }),
        });

        if (!string.Equals(verification.Outcome, "passed", StringComparison.OrdinalIgnoreCase))
        {
            await Status("failed", "Verify", "verification did not pass");
            return $"failed:{request.AppId}";
        }

        // Dev PO gate auto-approves → ready-for-review is transient → live (fires the publish email).
        await Status("ready-for-review", "Review");
        await Status("live", "Live");
        return $"live:{request.AppId}:{result.HostedUrl}";
    }

    private static (string Owner, string Name) SplitFullName(string fullName)
    {
        var i = fullName.IndexOf('/');
        return i > 0 ? (fullName[..i], fullName[(i + 1)..]) : (string.Empty, fullName);
    }
}
