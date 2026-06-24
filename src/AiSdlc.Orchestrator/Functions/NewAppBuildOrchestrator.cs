using AiSdlc.Orchestrator.Builds;
using AiSdlc.RepoIndex.Charter;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// New-path build orchestrator (Phase 1) — entry point for API-initiated builds, where the Charter
/// arrives via create-build and no repo exists yet. Pipeline to come (subsequent components):
/// derive capability profile (deterministic stackProfile + axes) → create repo from template (GitHub App)
/// → Call 1 /provision + handle the Call-2 result → write deploy.yml + repo vars + Clerk key → build →
/// verification gate → emit /status, /runtime, /verification callbacks to Yorrixx.
/// Currently a skeleton that accepts and records the request.
/// </summary>
public static class NewAppBuildOrchestrator
{
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
        // TODO (components 4-6): /provision(capabilities) → write deploy.yml + vars → build → verify → callbacks.
        return $"repo:{repo.FullName}";
    }
}
