namespace AiSdlc.Orchestrator.Builds;

/// <summary>Input to the repo-create activity: which app, and the resolved stack profile (selects template).</summary>
public sealed record CreateRepoInput(string AppId, string StackProfile);
