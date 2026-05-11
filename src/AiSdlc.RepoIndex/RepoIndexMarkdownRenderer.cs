using System.Text;

namespace AiSdlc.RepoIndex;

public static class RepoIndexMarkdownRenderer
{
    public static string Render(RepoIndex index)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Repository: {index.Repository}");
        if (!string.IsNullOrWhiteSpace(index.Description))
            sb.AppendLine(index.Description);
        sb.AppendLine();

        sb.AppendLine("### Tech Stack");
        if (index.Stack.Frontend is { } fe)
            sb.AppendLine($"- **Frontend:** {fe.Framework} + {fe.Language} (`{fe.Location}`)");
        if (index.Stack.Api is { } api)
            sb.AppendLine($"- **API:** {api.Framework} (`{api.Location}`)");
        if (index.Stack.Database is { } db)
            sb.AppendLine($"- **Database:** {db.Engine} via {db.Orm}");
        sb.AppendLine();

        if (index.Pages.Count > 0)
        {
            sb.AppendLine("### Pages");
            foreach (var p in index.Pages)
                sb.AppendLine($"- `{p.Path}` → **{p.Component}** — {p.Description}");
            sb.AppendLine();
        }

        if (index.ApiEndpoints.Count > 0)
        {
            sb.AppendLine("### API Endpoints");
            foreach (var e in index.ApiEndpoints)
                sb.AppendLine($"- {e}");
            sb.AppendLine();
        }

        if (index.DatabaseTables.Count > 0)
        {
            sb.AppendLine("### Database Tables");
            sb.AppendLine(string.Join(", ", index.DatabaseTables));
            sb.AppendLine();
        }

        if (index.HighRiskPaths.Count > 0 || index.MediumRiskPaths.Count > 0 || index.LowRiskPaths.Count > 0)
        {
            sb.AppendLine("### Risk Areas");
            if (index.HighRiskPaths.Count > 0)
                sb.AppendLine($"- **High:** {string.Join(", ", index.HighRiskPaths)}");
            if (index.MediumRiskPaths.Count > 0)
                sb.AppendLine($"- **Medium:** {string.Join(", ", index.MediumRiskPaths)}");
            if (index.LowRiskPaths.Count > 0)
                sb.AppendLine($"- **Low:** {string.Join(", ", index.LowRiskPaths)}");
        }

        return sb.ToString().TrimEnd();
    }
}
