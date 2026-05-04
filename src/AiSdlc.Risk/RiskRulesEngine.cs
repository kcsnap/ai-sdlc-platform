using AiSdlc.Shared;

namespace AiSdlc.Risk;

public sealed class RiskRulesEngine : IRiskRulesEngine
{
    public RiskAssessmentResult Assess(RiskAssessmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var triggeredSignals = new List<RiskSignal>();
        var triggeredRules = new List<RiskRule>();

        var changedFilePaths = request.ChangedFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .ToArray();

        var affectedAreas = request.AffectedAreas
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Select(area => area.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (changedFilePaths.Length == 0 && affectedAreas.Count == 0)
        {
            AddRule(
                triggeredSignals,
                triggeredRules,
                "unknown-change-scope",
                "The change scope is empty or ambiguous.",
                RiskLevel.Unknown);
        }

        foreach (var qualityGate in request.QualityGateResults.Where(result => result.IsMandatory && !result.Passed))
        {
            AddRule(
                triggeredSignals,
                triggeredRules,
                "mandatory-quality-gate-failed",
                $"Mandatory quality gate '{qualityGate.Name}' failed.",
                RiskLevel.High);
        }

        if (request.TerraformChanged || changedFilePaths.Any(path => path.StartsWith("infra/terraform/", StringComparison.OrdinalIgnoreCase)))
        {
            AddRule(triggeredSignals, triggeredRules, "terraform-change", "Terraform infrastructure changed.", RiskLevel.Medium);
        }

        if (request.DatabaseMigrationsChanged || changedFilePaths.Any(IsDatabaseMigrationPath))
        {
            AddRule(triggeredSignals, triggeredRules, "database-migration-change", "Database migration assets changed.", RiskLevel.Medium);
        }

        if (request.AuthenticationChanged || affectedAreas.Contains("authentication") || affectedAreas.Contains("authorization") || ContainsAny(changedFilePaths, "auth", "identity", "login", "oauth"))
        {
            AddRule(triggeredSignals, triggeredRules, "authentication-change", "Authentication or authorisation logic changed.", RiskLevel.High);
        }

        if (request.PaymentChanged || affectedAreas.Contains("payment") || affectedAreas.Contains("checkout") || ContainsAny(changedFilePaths, "payment", "checkout", "billing"))
        {
            AddRule(triggeredSignals, triggeredRules, "payment-change", "Payment or checkout logic changed.", RiskLevel.High);
        }

        if (request.SecurityChanged || affectedAreas.Contains("security") || ContainsAny(changedFilePaths, "security", "permission", "secret", "keyvault", "key-vault"))
        {
            AddRule(triggeredSignals, triggeredRules, "security-sensitive-change", "Security-sensitive implementation changed.", RiskLevel.High);
        }

        if (request.PrivacyChanged || affectedAreas.Contains("privacy") || affectedAreas.Contains("personal-data") || ContainsAny(changedFilePaths, "privacy", "gdpr", "pii", "personal-data"))
        {
            AddRule(triggeredSignals, triggeredRules, "privacy-change", "Personal data handling changed.", RiskLevel.High);
        }

        if (changedFilePaths.Any(path => path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)))
        {
            AddRule(triggeredSignals, triggeredRules, "workflow-change", "GitHub Actions workflow files changed.", RiskLevel.Medium);
        }

        if (changedFilePaths.Any(IsApiPath))
        {
            AddRule(triggeredSignals, triggeredRules, "api-change", "API-facing backend code changed.", RiskLevel.Medium);
        }

        if (triggeredSignals.Count == 0 && changedFilePaths.Length > 0)
        {
            if (changedFilePaths.All(IsDocumentationPath))
            {
                AddRule(triggeredSignals, triggeredRules, "docs-only-change", "Only documentation files changed.", RiskLevel.Low);
            }
            else if (changedFilePaths.All(IsTestPath))
            {
                AddRule(triggeredSignals, triggeredRules, "tests-only-change", "Only automated tests changed.", RiskLevel.Low);
            }
            else if (changedFilePaths.All(IsSimpleFrontendPath))
            {
                AddRule(triggeredSignals, triggeredRules, "simple-frontend-change", "Only low-risk frontend or content files changed.", RiskLevel.Low);
            }
        }

        var riskLevel = DetermineRiskLevel(triggeredSignals);
        var decision = DetermineDecision(riskLevel, triggeredSignals);
        var rationale = BuildRationale(triggeredSignals, riskLevel, decision);

        return new RiskAssessmentResult
        {
            RiskLevel = riskLevel,
            Decision = decision,
            Rationale = rationale,
            TriggeredSignals = triggeredSignals,
            TriggeredRules = triggeredRules
        };
    }

    private static void AddRule(
        ICollection<RiskSignal> signals,
        ICollection<RiskRule> rules,
        string code,
        string description,
        RiskLevel level)
    {
        signals.Add(new RiskSignal
        {
            Code = code,
            Description = description,
            Level = level
        });

        rules.Add(new RiskRule
        {
            Code = code,
            Description = description
        });
    }

    private static RiskLevel DetermineRiskLevel(IEnumerable<RiskSignal> signals)
    {
        if (signals.Any(signal => signal.Level == RiskLevel.High))
        {
            return RiskLevel.High;
        }

        if (signals.Any(signal => signal.Level == RiskLevel.Unknown))
        {
            return RiskLevel.Unknown;
        }

        if (signals.Any(signal => signal.Level == RiskLevel.Medium))
        {
            return RiskLevel.Medium;
        }

        if (signals.Any(signal => signal.Level == RiskLevel.Low))
        {
            return RiskLevel.Low;
        }

        return RiskLevel.Unknown;
    }

    private static RiskDecision DetermineDecision(RiskLevel riskLevel, IEnumerable<RiskSignal> signals)
    {
        if (signals.Any(signal => signal.Code == "mandatory-quality-gate-failed"))
        {
            return RiskDecision.StopWorkflow;
        }

        return riskLevel switch
        {
            RiskLevel.Low => RiskDecision.ContinueAutonomously,
            RiskLevel.Medium => RiskDecision.RequireHumanReview,
            RiskLevel.High => RiskDecision.RequireHumanReview,
            _ => RiskDecision.RequireHumanReview
        };
    }

    private static string BuildRationale(
        IReadOnlyCollection<RiskSignal> signals,
        RiskLevel riskLevel,
        RiskDecision decision)
    {
        if (signals.Count == 0)
        {
            return $"No deterministic rules matched. Risk level: {riskLevel}. Decision: {decision}.";
        }

        return $"Risk level: {riskLevel}. Decision: {decision}. Signals: {string.Join("; ", signals.Select(signal => signal.Description))}";
    }

    private static bool ContainsAny(IEnumerable<string> values, params string[] fragments) =>
        values.Any(value => fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

    private static bool IsDocumentationPath(string path) =>
        path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestPath(string path) =>
        path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(".Tests/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSimpleFrontendPath(string path) =>
        path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);

    private static bool IsApiPath(string path) =>
        path.Contains("/controllers/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/api/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsDatabaseMigrationPath(string path) =>
        path.Contains("/migrations/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
}
