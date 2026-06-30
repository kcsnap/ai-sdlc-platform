namespace Yorrixx.Provisioner.Internal;

/// Inbound auth for the provisioner's HTTP functions: the platform presents X-Platform-Provision-Key on
/// every call; the host validates it against this value (sourced from Key Vault at deploy time).
public sealed class ProvisionerOptions
{
    public const string SectionName = "Provisioner";

    public string InboundKey { get; init; } = "";

    /// Storage account backing the provision-status table (and AzureWebJobsStorage queues). Accessed via
    /// the provisioner's managed identity.
    public string StorageAccountName { get; init; } = "";
}

/// Subscription + resource group the provision worker stamps into the provision-result. Reuses the
/// Hosting config section (same Subscription/RG the Hosting module provisions into).
public sealed class ProvisionWorkerOptions
{
    public const string SectionName = "Hosting";
    public string SubscriptionId { get; init; } = "";
    public string ResourceGroup { get; init; } = "";
}
