namespace AiSdlc.Agents;

internal static class BusinessAnalystMarkdownRenderer
{
    public static string Render(BusinessAnalystChangeRequest request, IReadOnlyList<string> followUpQuestions)
    {
        var impactAreas = BuildImpactAreas(request);
        var acceptanceCriteria = BuildAcceptanceCriteria(request);
        var constraints = SplitLinesOrBullets(request.Constraints);
        var references = SplitLinesOrBullets(request.ReferenceMaterial);

        return string.Join(
            Environment.NewLine,
            [
                "# Business Analyst Review",
                string.Empty,
                "## Change Summary",
                $"- **Title:** {Fallback(request.Title, "Untitled change request")}",
                $"- **Request:** {Fallback(request.ChangeRequest, "Not provided")}",
                $"- **Business need:** {Fallback(request.BusinessNeed, "Not provided")}",
                $"- **Target user:** {Fallback(request.TargetUser, "Not provided")}",
                $"- **App type:** {Fallback(request.AppType, "Not provided")}",
                string.Empty,
                "## Existing Product Context",
                Fallback(request.ExistingProductContext, "No existing product context was supplied. Validate the requested change against the live product before development starts."),
                string.Empty,
                "## Recommended Developer Scope",
                $"- Implement the requested change in the existing product without broadening scope beyond the spec.",
                $"- Preserve current behaviour outside the impacted areas: {string.Join(", ", impactAreas)}.",
                $"- Confirm constraints before coding: {Fallback(request.Constraints, "No explicit constraints provided.")}",
                string.Empty,
                "## Acceptance Criteria Draft",
                string.Join(Environment.NewLine, acceptanceCriteria.Select(item => $"- {item}")),
                string.Empty,
                "## Impacted Areas",
                string.Join(Environment.NewLine, impactAreas.Select(item => $"- {item}")),
                string.Empty,
                "## Constraints",
                string.Join(Environment.NewLine, constraints.Select(item => $"- {item}")),
                string.Empty,
                "## References",
                string.Join(Environment.NewLine, references.Select(item => $"- {item}")),
                string.Empty,
                "## Developer Handoff",
                "- Review the existing product flow for the impacted areas before implementation.",
                "- Keep implementation aligned to the requested user outcome and avoid unrelated refactors in the first slice.",
                "- Update tests for the changed behaviour and document any assumptions in the PR description.",
                string.Empty,
                "## Follow-up Questions",
                followUpQuestions.Count == 0
                    ? "- None. The current spec is sufficient for an initial implementation slice."
                    : string.Join(Environment.NewLine, followUpQuestions.Select(item => $"- {item}"))
            ]);
    }

    private static IReadOnlyList<string> BuildAcceptanceCriteria(BusinessAnalystChangeRequest request)
    {
        var items = new List<string>
        {
            $"The change described as '{Fallback(request.ChangeRequest, "the requested update")}' is visible in the existing product.",
            $"The experience works for the target user: {Fallback(request.TargetUser, "the intended user")}.",
            "No unrelated existing product behaviour regresses in the impacted areas."
        };

        if (!string.IsNullOrWhiteSpace(request.DefinitionOfDone))
        {
            items.Add(request.DefinitionOfDone.Trim());
        }

        return items;
    }

    private static IReadOnlyList<string> BuildImpactAreas(BusinessAnalystChangeRequest request)
    {
        var text = $"{request.ChangeRequest} {request.ExistingProductContext} {request.DefinitionOfDone}".ToLowerInvariant();
        var areas = new List<string>();

        if (text.Contains("product"))
        {
            areas.Add("product detail experience");
        }

        if (text.Contains("catalog") || text.Contains("listing"))
        {
            areas.Add("catalog or listing pages");
        }

        if (text.Contains("checkout") || text.Contains("purchase") || text.Contains("enquiry"))
        {
            areas.Add("purchase or enquiry flow");
        }

        if (text.Contains("admin"))
        {
            areas.Add("admin management screens");
        }

        if (text.Contains("api") || text.Contains("backend"))
        {
            areas.Add("API or backend service layer");
        }

        if (areas.Count == 0)
        {
            areas.Add("the user-facing flow described in the spec");
        }

        return areas.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> SplitLinesOrBullets(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ["None provided"];
        }

        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.TrimStart('-', '*', ' '))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .DefaultIfEmpty("None provided")
            .ToArray();
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
