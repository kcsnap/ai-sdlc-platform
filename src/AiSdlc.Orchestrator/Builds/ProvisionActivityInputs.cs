using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;
using Yorrixx.Provisioner.Contracts;

namespace AiSdlc.Orchestrator.Builds;

// Platform-side provisioning glue that is NOT part of the canonical provisioner⇄platform wire contract
// (ADR-XR-0001 A6): the wire types live in Yorrixx.Provisioner.Contracts; these are orchestrator
// activity inputs and the platform-only mapping from the resolved capability profile.

/// <summary>Input to the commit-deploy-workflow activity: commit the canonical deploy.yml verbatim.</summary>
public sealed record CommitDeployInput(string Repository, string DeployYaml, string Branch);

/// <summary>
/// Maps the platform's resolved FullStack capability profile to the dependency-free provision wire shape.
/// Replaces the old <c>ProvisionCapabilities.From</c> factory, which coupled the contract to
/// <see cref="CapabilityProfile"/>; the contract is now standalone, so the coupling lives here.
/// </summary>
public static class ProvisionCapabilitiesMapper
{
    public static ProvisionCapabilities ToProvisionCapabilities(this CapabilityProfile p) =>
        new(p.Auth, p.Database, p.Payments, p.Email, p.AIApi);
}
