using System.Text;

namespace AiSdlc.RepoIndex.Charter;

public static class CharterMarkdownRenderer
{
    public static string Render(Charter charter)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## App Charter (v{charter.SchemaVersion})");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(charter.Identity.AppName))
            sb.AppendLine($"**App:** {charter.Identity.AppName}");
        if (!string.IsNullOrWhiteSpace(charter.Identity.OneLineDescription))
            sb.AppendLine($"**Description:** {charter.Identity.OneLineDescription}");
        sb.AppendLine();

        sb.AppendLine("### Audience");
        if (!string.IsNullOrWhiteSpace(charter.Audience.PrimaryUserDescription))
            sb.AppendLine($"- **Primary user:** {charter.Audience.PrimaryUserDescription}");
        if (charter.Audience.ExpectedScale != ExpectedScale.Unknown)
            sb.AppendLine($"- **Expected scale:** {charter.Audience.ExpectedScale}");
        sb.AppendLine();

        sb.AppendLine("### Purpose");
        if (!string.IsNullOrWhiteSpace(charter.Purpose.ProblemBeingSolved))
            sb.AppendLine($"- **Problem:** {charter.Purpose.ProblemBeingSolved}");
        if (charter.Purpose.SuccessCriteria.Count > 0)
        {
            sb.AppendLine("- **Success criteria:**");
            foreach (var c in charter.Purpose.SuccessCriteria)
                sb.AppendLine($"  - {c}");
        }
        sb.AppendLine();

        if (charter.Features.Count > 0)
        {
            sb.AppendLine("### Features");
            foreach (var f in charter.Features)
            {
                var tag = f.Priority != FeaturePriority.Unknown ? $" ({f.Priority})" : string.Empty;
                var status = f.Status != FeatureStatus.Unknown ? $" [{f.Status}]" : string.Empty;
                sb.AppendLine($"- **{f.Name}**{tag}{status} — {f.Description}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### Constraints");
        if (charter.Constraints.DataSensitivity != DataSensitivity.Unknown)
            sb.AppendLine($"- **Data sensitivity:** {charter.Constraints.DataSensitivity}");
        sb.AppendLine($"- Needs auth: {YesNo(charter.Constraints.NeedsAuth)}");
        sb.AppendLine($"- Needs payments: {YesNo(charter.Constraints.NeedsPayments)}");
        sb.AppendLine($"- Needs email: {YesNo(charter.Constraints.NeedsEmail)}");
        sb.AppendLine($"- Needs AI API: {YesNo(charter.Constraints.NeedsAIApi)}");
        sb.AppendLine();

        if (charter.Integrations.Count > 0)
        {
            sb.AppendLine("### Integrations");
            foreach (var i in charter.Integrations)
                sb.AppendLine($"- {i}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(charter.AdditionalContext))
        {
            sb.AppendLine("### Additional context");
            sb.AppendLine(charter.AdditionalContext);
        }

        return sb.ToString().TrimEnd();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
