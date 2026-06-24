using AiSdlc.RepoIndex.Charter;

namespace AiSdlc.Orchestrator.Builds;

// Wire shapes for the provisioner⇄platform contract (responsibility-split-phase1-provisioner-contract.md).
// Call 1: platform → provisioner (POST /provision). Call 2: provisioner → platform (/api/provision-result),
// with GET /provision/{buildId} as the poll fallback.

/// <summary>Call 1 — declarative provision request. The provisioner owns the resource topology.</summary>
public sealed record ProvisionRequest
{
    public required string AppId { get; init; }
    public required string BuildId { get; init; }
    public string Env { get; init; } = "dev";
    public string Region { get; init; } = "northeurope";
    public required string StackProfile { get; init; }
    public required ProvisionCapabilities Capabilities { get; init; }
    public required ProvisionRepo Repo { get; init; }
}

public sealed record ProvisionCapabilities(bool Auth, bool Database, bool Payments, bool Email, bool AiApi)
{
    /// <summary>Maps the resolved FullStack capability profile to the provision wire shape.</summary>
    public static ProvisionCapabilities From(CapabilityProfile p) =>
        new(p.Auth, p.Database, p.Payments, p.Email, p.AIApi);
}

public sealed record ProvisionRepo(string Owner, string Name, string DefaultBranch);

/// <summary>Call 2 — provision result. Deploy identity is an App-registration (OIDC); no secret on the wire.</summary>
public sealed record ProvisionResult
{
    public string AppId { get; init; } = string.Empty;
    public string BuildId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;   // "provisioned" | "failed"
    public IReadOnlyList<ProvisionResource> Resources { get; init; } = Array.Empty<ProvisionResource>();
    public string? HostedUrl { get; init; }
    public ProvisionDeploy? Deploy { get; init; }
    public ProvisionClerk? Clerk { get; init; }
    public string? Detail { get; init; }

    /// <summary>
    /// The canonical, fully-resolved deploy workflow (static/full-stack resolved, OIDC-no-secret, correct
    /// serve/zip handling). The platform commits this verbatim to .github/workflows/deploy.yml — it does
    /// NOT render its own. The OIDC triple / resource names / clerk key are already baked in.
    /// </summary>
    public string? DeployYaml { get; init; }
}

public sealed record ProvisionResource(string Kind, string Name, string ResourceId);

/// <summary>Input to the commit-deploy-workflow activity: commit the canonical deploy.yml verbatim.</summary>
public sealed record CommitDeployInput(string Repository, string DeployYaml, string Branch);

public sealed record ProvisionDeploy
{
    public string Method { get; init; } = "oidc-federated";
    public string? ClientId { get; init; }
    public string? TenantId { get; init; }
    public string? SubscriptionId { get; init; }
}

// Single Clerk instance, per-app isolation = Organization → the secret is instance-wide (a KV ref), the
// publishable key is the instance key used with the per-app Org.
public sealed record ProvisionClerk
{
    public string? PublishableKey { get; init; }
    public string? SecretKeyVaultRef { get; init; }
}
