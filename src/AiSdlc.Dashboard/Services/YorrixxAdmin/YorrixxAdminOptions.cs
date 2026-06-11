namespace AiSdlc.Dashboard.Services.YorrixxAdmin;

public sealed class YorrixxAdminOptions
{
    public const string SectionName = "YorrixxAdmin";

    // Base URL of the Yorrixx admin API, e.g.
    // https://ca-yorrixx-dev-api.proudpebble-018b8327.uksouth.azurecontainerapps.io
    public string BaseUrl { get; set; } = string.Empty;

    // API key sent in the X-Yorrixx-Admin-Key header. Loaded from user-secrets
    // in development; from Key Vault / app config in deployed environments.
    public string ApiKey { get; set; } = string.Empty;
}
