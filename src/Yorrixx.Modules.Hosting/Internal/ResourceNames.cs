using System.Text;

namespace Yorrixx.Modules.Hosting.Internal;

/// Centralises the per-user-app resource naming convention from ADR-0002
/// (amended by ADR-0003): `{kind}-{appNameSlug8}-{appId8}` where the slug
/// is the first 8 alphanumerics of the charter app name (fallback `app`)
/// and `appId8` is the first 8 hex of the GUID-no-hyphens.
///
/// Every name produced here is deterministic from (appId, appName) so the
/// hosting service can re-derive names on subsequent runs and recognise
/// resources it provisioned earlier.
internal sealed record ResourceNames(string Slug8, string Id8)
{
    public string AppServicePlan      => $"plan-{Slug8}-{Id8}";
    public string FrontendWebApp      => $"app-{Slug8}-{Id8}-frontend";
    // Static profile: an Azure Static Web App (no App Service plan) instead of
    // the F1 Web App — sidesteps the F1 per-region plan cap + instance-capacity
    // wall. Hosting carries this name in DeployedApp.FrontendWebAppName so the
    // deploy workflow targets it without extra plumbing.
    public string StaticWebApp        => $"swa-{Slug8}-{Id8}";
    public string FlexConsumptionPlan => $"flex-{Slug8}-{Id8}";
    public string FunctionApp         => $"func-{Slug8}-{Id8}";

    // Storage account names: 3-24 chars, lowercase alphanumerics only, no
    // hyphens. `st` + 8 hex = 10 chars, comfortably inside the limit.
    public string StorageAccount      => $"st{Id8}";

    public string ManagedIdentity     => $"id-{Slug8}-{Id8}";
    public string AppInsights         => $"appi-{Slug8}-{Id8}";

    // Azure auto-creates this smart-detector alert rule alongside every App
    // Insights component and does NOT remove it when the component is deleted,
    // so deprovision has to delete it explicitly.
    public string FailureAnomaliesAlertRule => $"Failure Anomalies - {AppInsights}";
    public string CosmosContainer     => $"app-{Slug8}-{Id8}";
    public string KvSecretPrefix      => $"app-{Id8}--";
    public string DeployServicePrincipal => $"sp-userapp-{Id8}";

    public string RepoName(string repoNamePrefix) => $"{repoNamePrefix}-{Id8}";

    public static ResourceNames From(string appId, string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var slug = Slugify(appName);
        var slug8 = string.IsNullOrEmpty(slug) ? "app" : slug[..Math.Min(8, slug.Length)];

        // Strip hyphens in case the caller passed a GUID with them. Lowercase
        // because both Azure resource names and the federated credential
        // subject are case-sensitive.
        var idClean = appId.Replace("-", "").ToLowerInvariant();
        var id8 = idClean.Length >= 8 ? idClean[..8] : idClean.PadRight(8, '0');

        return new ResourceNames(slug8, id8);
    }

    private static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
