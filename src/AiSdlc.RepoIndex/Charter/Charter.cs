namespace AiSdlc.RepoIndex.Charter;

// Mirrors the .yorrixx/charter.json schema written by yorrixx-app.
// PascalCase property names round-trip cleanly with System.Text.Json defaults
// (no naming policy on the Yorrixx side either).

public sealed record Charter
{
    public int SchemaVersion { get; init; }
    public CharterIdentity Identity { get; init; } = new();
    public CharterAudience Audience { get; init; } = new();
    public CharterPurpose Purpose { get; init; } = new();
    public IReadOnlyList<CharterFeature> Features { get; init; } = Array.Empty<CharterFeature>();
    public CharterConstraints Constraints { get; init; } = new();
    public IReadOnlyList<string> Integrations { get; init; } = Array.Empty<string>();
    public string AdditionalContext { get; init; } = string.Empty;
}

public sealed record CharterIdentity
{
    public string AppName { get; init; } = string.Empty;
    public string OneLineDescription { get; init; } = string.Empty;
}

public sealed record CharterAudience
{
    public string PrimaryUserDescription { get; init; } = string.Empty;
    public ExpectedScale ExpectedScale { get; init; }
}

public sealed record CharterPurpose
{
    public string ProblemBeingSolved { get; init; } = string.Empty;
    public IReadOnlyList<string> SuccessCriteria { get; init; } = Array.Empty<string>();
}

public sealed record CharterFeature
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public FeatureStatus Status { get; init; }
    public string AddedIn { get; init; } = string.Empty;
    public FeaturePriority Priority { get; init; }
}

public sealed record CharterConstraints
{
    public DataSensitivity DataSensitivity { get; init; }
    public bool NeedsAuth { get; init; }
    public bool NeedsPayments { get; init; }
    public bool NeedsEmail { get; init; }
    public bool NeedsAIApi { get; init; }
}

// Each enum has an explicit Unknown=0 default so a missing field deserialises predictably.
// Strict deserialisation (JsonStringEnumConverter throws on unknown strings) is wrapped in
// try/catch inside GitHubCharterReader so a malformed charter is logged and treated as absent.

public enum ExpectedScale
{
    Unknown = 0,
    Solo,
    SmallTeam,
    Public
}

public enum DataSensitivity
{
    Unknown = 0,
    Low,
    Medium,
    High
}

public enum FeatureStatus
{
    Unknown = 0,
    Planned,
    Built,
    Removed
}

public enum FeaturePriority
{
    Unknown = 0,
    MustHave,
    NiceToHave
}
