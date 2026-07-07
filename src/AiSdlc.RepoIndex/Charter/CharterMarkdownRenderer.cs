using System.Text;

namespace AiSdlc.RepoIndex.Charter;

// Inside this namespace the simple name "Charter" binds to the namespace itself, so the contract-package
// import must live INSIDE the namespace body (inner-scope usings win the lookup).
using Yorrixx.Contracts.Generation;

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
        // Package enums have no Unknown sentinel (a missing field deserializes to the first member),
        // so scale/priority/status/sensitivity render unconditionally now.
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
                sb.AppendLine($"- **{f.Name}** ({f.Priority}) [{f.Status}] — {f.Description}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### Constraints");
        sb.AppendLine($"- **Data sensitivity:** {charter.Constraints.DataSensitivity}");
        sb.AppendLine($"- Needs auth: {YesNo(charter.Constraints.NeedsAuth)}");
        sb.AppendLine($"- Needs payments: {YesNo(charter.Constraints.NeedsPayments)}");
        sb.AppendLine($"- Needs email: {YesNo(charter.Constraints.NeedsEmail)}");
        sb.AppendLine($"- Needs AI API: {YesNo(charter.Constraints.NeedsAIApi)}");
        sb.AppendLine();

        if (charter.Integrations.Count > 0)
        {
            sb.AppendLine("### Integrations");
            // Integrations are structured in the contract package (Name + Purpose), not bare strings.
            foreach (var i in charter.Integrations)
                sb.AppendLine(string.IsNullOrWhiteSpace(i.Purpose) ? $"- {i.Name}" : $"- **{i.Name}** — {i.Purpose}");
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
