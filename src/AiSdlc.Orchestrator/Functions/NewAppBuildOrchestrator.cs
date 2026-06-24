using System.Threading;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.RepoIndex.Charter;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// New-path build orchestrator (Phase 1) — entry point for API-initiated builds, where the Charter
/// arrives via create-build and no repo exists yet. Pipeline:
/// derive profile (deterministic stackProfile) → create repo from template (GitHub App) → Call 1
/// /provision + handle the Call-2 result (poll fallback) → [4b] write deploy.yml + repo vars + Clerk key
/// → [5] build → [6] verification gate + /status, /runtime, /verification callbacks to Yorrixx.
/// </summary>
public static class NewAppBuildOrchestrator
{
    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DeployPollInterval = TimeSpan.FromSeconds(30);
    private const int MaxDeployPolls = 40;   // ~20 min for the deploy workflow to finish

    [Function(nameof(NewAppBuildOrchestrator))]
    public static async Task<string> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<CreateBuildRequest>()
            ?? throw new InvalidOperationException("Build input must include a CreateBuildRequest payload.");
        var charter = request.Charter
            ?? throw new InvalidOperationException("Build input must include a Charter.");

        // Component 2 — deterministic profile gate (no LLM): Static iff no backend need; else FullStack.
        var stackProfile = StackProfileResolver.Resolve(charter);

        // Component 3 — create the user-app repo from the stack-appropriate template (GitHub App).
        var repo = await context.CallActivityAsync<CreatedRepository>(
            nameof(BuildActivityFunctions.CreateUserAppRepoAsync),
            new CreateRepoInput(request.AppId, stackProfile.ToString()));
        context.SetCustomStatus($"repo-created:{stackProfile}");

        // Component 4 — provision cloud resources via the dedicated provisioner. Call 1 is async; the
        // Call-2 callback raises 'provision-result', and a GET poll is the fallback if it's dropped.
        var (repoOwner, repoName) = SplitFullName(repo.FullName);
        var capabilities = ProvisionCapabilities.From(
            CapabilityResolver.Resolve(charter, databaseDerived: charter.Constraints.NeedsPersistence));
        var provisionRequest = new ProvisionRequest
        {
            AppId        = request.AppId,
            BuildId      = context.InstanceId,
            StackProfile = stackProfile.ToString(),
            Capabilities = capabilities,
            Repo         = new ProvisionRepo(repoOwner, repoName, repo.DefaultBranch),
        };
        await context.CallActivityAsync(nameof(BuildActivityFunctions.StartProvisionAsync), provisionRequest);
        context.SetCustomStatus("provisioning");

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
            throw new InvalidOperationException(
                $"Provisioning failed for {request.AppId}: {result?.Detail ?? "no result before timeout"}.");

        context.SetCustomStatus($"provisioned:{result.HostedUrl}");

        // Component 4b — wire the deploy identity (OIDC) + Clerk key into the repo as Actions variables so
        // the template's deploy.yml can authenticate and deploy.
        await context.CallActivityAsync(
            nameof(BuildActivityFunctions.ApplyDeployConfigAsync),
            new ApplyDeployConfigInput(repo.FullName, result.Deploy, result.Clerk?.PublishableKey));
        context.SetCustomStatus("deploy-configured");

        // Component 5 — verification gate. The template's deploy.yml runs on the new repo; poll it to a
        // terminal state, then probe the hosted URL. (Agent-driven feature generation into the repo is a
        // deferred enhancement; this gate proves the new-path deploy + serve.)
        context.SetCustomStatus("verifying");
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
        context.SetCustomStatus($"verified:{verification.Outcome}");

        // TODO (6): emit /status, /runtime, /verification callbacks to Yorrixx (/runtime before /status:live).
        return $"verified:{request.AppId}:{verification.Outcome}";
    }

    private static (string Owner, string Name) SplitFullName(string fullName)
    {
        var i = fullName.IndexOf('/');
        return i > 0 ? (fullName[..i], fullName[(i + 1)..]) : (string.Empty, fullName);
    }
}
