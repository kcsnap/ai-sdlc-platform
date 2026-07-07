using Yorrixx.Contracts.Generation;

namespace AiSdlc.RepoIndex.Tests.Charter;

/// <summary>
/// Positional-record factory for the package Charter types (Yorrixx.Contracts.Charter has required
/// constructor parameters, unlike the old hand-mirrored init-property records).
/// </summary>
internal static class TestCharters
{
    public static Yorrixx.Contracts.Generation.Charter Make(
        int schemaVersion = 1,
        string appName = "X",
        string description = "",
        string primaryUser = "",
        ExpectedScale scale = ExpectedScale.Solo,
        string problem = "",
        IReadOnlyList<string>? successCriteria = null,
        IReadOnlyList<CharterFeature>? features = null,
        DataSensitivity sensitivity = DataSensitivity.Low,
        bool auth = false,
        bool payments = false,
        bool email = false,
        bool aiApi = false,
        bool persistence = false,
        IReadOnlyList<CharterIntegration>? integrations = null,
        string additionalContext = "") =>
        new(schemaVersion,
            new CharterIdentity(appName, description),
            new CharterAudience(primaryUser, scale),
            new CharterPurpose(problem, successCriteria ?? []),
            features ?? [],
            new CharterConstraints(sensitivity, auth, payments, email, aiApi, persistence),
            integrations ?? [])
        { AdditionalContext = additionalContext };
}
