using AiSdlc.Shared;

namespace AiSdlc.Risk;

public sealed class RiskRulesEngine : IRiskRulesEngine
{
    private static readonly IReadOnlyList<RiskRule> Rules =
    [
        // High risk — explicit flags
        new RiskRule
        {
            Code = "HIGH_AUTH",
            Level = RiskLevel.High,
            Description = "Authentication or authorisation code changed.",
            Matches = r => r.AuthOrAuthorisationChanged || FilesMatch(r, "auth", "authoris", "authoriz", "identity", "login", "permission", "role", "claim")
        },
        new RiskRule
        {
            Code = "HIGH_PAYMENT",
            Level = RiskLevel.High,
            Description = "Payment or checkout code changed.",
            Matches = r => r.PaymentOrCheckoutChanged || FilesMatch(r, "payment", "checkout", "billing", "stripe", "invoice")
        },
        new RiskRule
        {
            Code = "HIGH_PII",
            Level = RiskLevel.High,
            Description = "Personal data handling code changed.",
            Matches = r => r.PersonalDataHandlingChanged || FilesMatch(r, "gdpr", "personaldata", "pii", "datasubject")
        },
        new RiskRule
        {
            Code = "HIGH_SECRETS",
            Level = RiskLevel.High,
            Description = "Secrets or Key Vault configuration changed.",
            Matches = r => r.SecretsOrKeyVaultChanged || FilesMatch(r, "keyvault", "secretmanager", "credential")
        },

        // Medium risk — explicit flags
        new RiskRule
        {
            Code = "MEDIUM_DB_MIGRATION",
            Level = RiskLevel.Medium,
            Description = "Database migration changed.",
            Matches = r => r.DatabaseMigrationsChanged || FilesMatch(r, "/migrations/", ".sql")
        },
        new RiskRule
        {
            Code = "MEDIUM_TERRAFORM",
            Level = RiskLevel.Medium,
            Description = "Terraform infrastructure changed.",
            Matches = r => r.TerraformChanged || FilesMatch(r, ".tf", "/infra/")
        },
        new RiskRule
        {
            Code = "MEDIUM_GITHUB_ACTIONS",
            Level = RiskLevel.Medium,
            Description = "GitHub Actions workflow changed.",
            Matches = r => r.GitHubActionsWorkflowsChanged || FilesMatch(r, ".github/workflows/")
        },
        new RiskRule
        {
            Code = "MEDIUM_API",
            Level = RiskLevel.Medium,
            Description = "API or backend service layer changed.",
            Matches = r => FilesMatch(r, "controller", "/api/", "endpoint", "service") && !OnlyLowRiskFiles(r)
        },

        // Low risk — file-path heuristics (checked last, only meaningful when nothing higher fired)
        new RiskRule
        {
            Code = "LOW_DOCS_ONLY",
            Level = RiskLevel.Low,
            Description = "Documentation-only change.",
            Matches = r => r.ChangedFilePaths.Count > 0 && r.ChangedFilePaths.All(IsDocFile)
        },
        new RiskRule
        {
            Code = "LOW_TESTS_ONLY",
            Level = RiskLevel.Low,
            Description = "Test-only change.",
            Matches = r => r.ChangedFilePaths.Count > 0 && r.ChangedFilePaths.All(IsTestFile)
        },
        new RiskRule
        {
            Code = "LOW_FRONTEND_CONTENT",
            Level = RiskLevel.Low,
            Description = "Simple frontend or content change.",
            Matches = r => r.ChangedFilePaths.Count > 0 && r.ChangedFilePaths.All(IsFrontendFile)
                        && !r.ChangedFilePaths.Any(f => IsTestFile(f) || IsDocFile(f))
        }
    ];

    public RiskAssessmentResult Assess(RiskAssessmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.QualityGatesPassed)
        {
            return new RiskAssessmentResult
            {
                Level = RiskLevel.High,
                Decision = RiskDecision.StopWorkflow,
                Rationale = "Mandatory quality gates have not passed. Autonomous continuation is blocked.",
                TriggeredSignals = [new RiskSignal { Code = "BLOCKED_QUALITY_GATES", Level = RiskLevel.High, Description = "Quality gates failed." }]
            };
        }

        var triggered = Rules
            .Where(rule => rule.Matches(request))
            .Select(rule => new RiskSignal { Code = rule.Code, Level = rule.Level, Description = rule.Description })
            .ToList();

        if (triggered.Count == 0)
        {
            return new RiskAssessmentResult
            {
                Level = RiskLevel.Unknown,
                Decision = RiskDecision.StopWorkflow,
                Rationale = "No risk signals matched the change. Manual review is required before continuing.",
                TriggeredSignals = []
            };
        }

        var highest = triggered.Max(s => s.Level);

        return highest switch
        {
            RiskLevel.High => new RiskAssessmentResult
            {
                Level = RiskLevel.High,
                Decision = RiskDecision.RequireHumanReview,
                Rationale = BuildRationale(triggered),
                TriggeredSignals = triggered
            },
            RiskLevel.Medium => new RiskAssessmentResult
            {
                Level = RiskLevel.Medium,
                Decision = RiskDecision.RequireHumanReview,
                Rationale = BuildRationale(triggered),
                TriggeredSignals = triggered
            },
            _ => new RiskAssessmentResult
            {
                Level = RiskLevel.Low,
                Decision = RiskDecision.ContinueAutonomously,
                Rationale = BuildRationale(triggered),
                TriggeredSignals = triggered
            }
        };
    }

    private static bool FilesMatch(RiskAssessmentRequest request, params string[] fragments) =>
        request.ChangedFilePaths.Any(path =>
            fragments.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase)));

    private static bool OnlyLowRiskFiles(RiskAssessmentRequest request) =>
        request.ChangedFilePaths.Count > 0 &&
        request.ChangedFilePaths.All(f => IsDocFile(f) || IsTestFile(f) || IsFrontendFile(f));

    private static bool IsDocFile(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/docs/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestFile(string path) =>
        path.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(".tests/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("test/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsFrontendFile(string path) =>
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/frontend/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/components/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/pages/", StringComparison.OrdinalIgnoreCase);

    private static string BuildRationale(IReadOnlyList<RiskSignal> signals) =>
        string.Join(" ", signals.Select(s => s.Description));
}
