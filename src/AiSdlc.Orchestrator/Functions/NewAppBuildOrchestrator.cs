using AiSdlc.Orchestrator.Builds;
using AiSdlc.RepoIndex.Charter;
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
    public static Task<string> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<CreateBuildRequest>()
            ?? throw new InvalidOperationException("Build input must include a CreateBuildRequest payload.");
        var charter = request.Charter
            ?? throw new InvalidOperationException("Build input must include a Charter.");

        // Component 2 — deterministic profile gate (no LLM): Static iff no backend need; else FullStack.
        // The repo template (component 3) and the /provision capabilities (component 4) follow from this.
        var stackProfile = StackProfileResolver.Resolve(charter);

        context.SetCustomStatus(stackProfile.ToString());
        // TODO (components 3-6): create repo (template per stackProfile) → /provision → build → verify → callbacks.
        return Task.FromResult($"resolved:{request.AppId}:{stackProfile}");
    }
}
