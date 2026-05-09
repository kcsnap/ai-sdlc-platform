namespace AiSdlc.Risk;

public interface IRiskRulesEngine
{
    RiskAssessmentResult Assess(RiskAssessmentRequest request);
}
