namespace AiSdlc.Orchestrator.Builds;

/// <summary>One verification check result. Mirrors the /verification callback check shape.</summary>
public sealed record VerificationCheck(string CheckId, string Name, string Status, string Evidence);
// Status: "pass" | "fail" | "skipped".

/// <summary>The verification gate outcome (drives the /verification callback + whether the build goes live).</summary>
public sealed record VerificationResult(string Outcome, IReadOnlyList<VerificationCheck> Checks);
// Outcome: "passed" | "failed".

/// <summary>Input to the deploy-status activity.</summary>
public sealed record DeployStatusInput(string Repository, string Reference);
