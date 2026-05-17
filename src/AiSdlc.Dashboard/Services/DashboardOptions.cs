namespace AiSdlc.Dashboard.Services;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    // Azure Storage account name used by the orchestrator's IAuditService.
    // When UseDevelopmentStorage is true this is ignored.
    public string AuditStorageAccountName { get; set; } = string.Empty;

    public bool UseDevelopmentStorage { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 2;

    // How many events to load per poll (and on initial backfill). Match MaxEventsInMemory so a
    // single startup poll fills the in-memory ring buffer for a busy day.
    public int BackfillSize { get; set; } = 500;

    // How far back the dashboard looks on startup. Set generously so a freshly-opened dashboard
    // shows the last day's activity even if no new events arrive immediately.
    public int BackfillHours { get; set; } = 24;

    public int MaxEventsInMemory { get; set; } = 500;
}
