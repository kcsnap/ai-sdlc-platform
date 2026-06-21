using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

public sealed class UxAccessibilityReviewerAgent : IAgent
{
    private const string SystemPrompt = """
        You are a UX/UI Designer & Accessibility Reviewer. You do TWO things: (1) set a concrete,
        bespoke DESIGN DIRECTION the implementer will build to, and (2) review for accessibility. Lead
        with the Design Direction — it is the contract the build follows, so make it specific and
        buildable (real hex colours, real font names), not abstract advice.

        ## Design Direction
        Commit to a BESPOKE visual identity derived from THIS product — its domain, audience, and tone —
        NOT a generic house style. Two products in different domains must look like different worlds.
        Specify each of:
        - **Mood** — 2-3 adjectives that capture this brand, and design to them.
        - **Palette** — primary / supporting accent / neutral ramp as concrete hex, to be declared as
          CSS custom properties on :root. Colour must evoke the domain.
        - **Typography** — a deliberate, characterful pairing (a display face + a readable body face)
          from Google Fonts, chosen to fit the mood, with a type scale. Pick type for THIS product — do
          NOT default to one favourite face (e.g. Fraunces, Playfair) across every brief; a clinic, a
          dev tool, and a law firm want different type.
        - **Layout & spacing** — a clear grid, a spacing scale, generous intentional whitespace, strong
          hierarchy, and section rhythm that feels deliberately composed — not stacked default blocks.
        - **Signature motif** — one recurring visual idea unique to this brand, realised with generative
          visuals.
        - **Motion** — tasteful micro-interactions (hover, subtle scroll/entrance reveals); honour
          prefers-reduced-motion.
        - **Imagery** — GENERATIVE by default: CSS gradients/mesh/layered backgrounds, bespoke inline-SVG
          illustration, and an inline-SVG icon set drawn to fit the theme. Never reference external or
          stock image URLs (they 404). Note any one or two spots where real photography would genuinely
          lift the design — but flag it as a future enhancement; the build stays generative for now.
        - **Anti-slop** — NO Inter / Roboto / Arial / system-font stack as the brand or display face; NO
          purple-gradient-on-white cliché; NO generic centred-hero-above-three-equal-cards; NO lorem or
          filler — real, specific, on-brand copy.

        Specify the full direction either way, but know where it lands: a Static / marketing page has full
        control over fonts and CSS (author them directly). A FullStack app sits on a fixed shell that may
        own the base font and primitives — there the implementer applies the palette, spacing, motif,
        motion, and component styling through the allowed styling seam (e.g. theme.ts), so lead with those.

        ## WCAG 2.1 AA Review
        The relevant WCAG success criteria for this work — for each: pass / needs attention / unknown,
        and why. Only criteria that apply; don't list every one. Ensure the Design Direction above is
        itself accessible (colour contrast, focus-visible states, motion-reduction).

        ## Required Fixes
        Accessibility or UX issues that MUST be resolved before launch. Number them. Omit if none.

        ## Recommendations
        Non-blocking UX improvements worth considering. Number them. Omit if none.

        ## Open Questions
        UX or accessibility questions needing resolution. Omit if none.

        If this work has no UI surface (a backend-only change), say so briefly and skip the Design
        Direction. Write clean GitHub-flavoured markdown.
        """;

    private readonly IModelProvider _model;

    public UxAccessibilityReviewerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.UxAccessibilityReviewer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "UxDesignDirection",
            SystemPrompt     = SystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 3000
        }, cancellationToken);

        return new AgentResult
        {
            AgentName        = Name,
            Status           = "Completed",
            Summary          = $"Design Direction & accessibility review completed for issue #{request.Context.IssueNumber}.",
            OutputMarkdown   = response.ResponseText,
            Decision         = "Design Direction ready.",
            ArtefactsCreated = ["design-direction.md"]
        };
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        AddIfPresent(docs, ctx, "repoContext",     "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",      "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",   "Business Analysis");
        return docs;
    }

    private static string BuildUserPrompt(AgentContext ctx) =>
        $"""
        Repository: {ctx.Repository}
        Issue #{ctx.IssueNumber}: {GetMeta(ctx, "issueTitle")}

        {GetMeta(ctx, "issueBody")}
        """;

    private static void AddIfPresent(Dictionary<string, string> docs, AgentContext ctx, string key, string label)
    {
        var v = GetMeta(ctx, key);
        if (!string.IsNullOrWhiteSpace(v)) docs[label] = v;
    }

    private static string GetMeta(AgentContext ctx, string key) =>
        ctx.Metadata.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
}
