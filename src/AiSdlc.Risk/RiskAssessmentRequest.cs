namespace AiSdlc.Risk;

public sealed record RiskAssessmentRequest
{
    public IReadOnlyList<string> ChangedFilePaths { get; init; } = [];
    public IReadOnlyList<string> AffectedAreas { get; init; } = [];
    public bool QualityGatesPassed { get; init; } = true;
    public bool TerraformChanged { get; init; }
    public bool DatabaseMigrationsChanged { get; init; }
    public bool GitHubActionsWorkflowsChanged { get; init; }
    public bool AuthOrAuthorisationChanged { get; init; }
    public bool PaymentOrCheckoutChanged { get; init; }
    public bool PersonalDataHandlingChanged { get; init; }
    public bool SecretsOrKeyVaultChanged { get; init; }
}
