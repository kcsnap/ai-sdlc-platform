namespace AiSdlc.Risk;

public sealed record RiskAssessmentRequest
{
    public IReadOnlyList<string> ChangedFilePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AffectedAreas { get; init; } = Array.Empty<string>();
    public IReadOnlyList<QualityGateResult> QualityGateResults { get; init; } = Array.Empty<QualityGateResult>();
    public bool TerraformChanged { get; init; }
    public bool DatabaseMigrationsChanged { get; init; }
    public bool AuthenticationChanged { get; init; }
    public bool PaymentChanged { get; init; }
    public bool SecurityChanged { get; init; }
    public bool PrivacyChanged { get; init; }
}
