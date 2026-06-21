using System.Text;
using System.Text.Json;

namespace ThemeHarness;

/// <summary>
/// The Tier-1 generation contract: turn a customer brief into a purely-visual,
/// self-contained, themed marketing site (HTML + CSS only). This is the lever we
/// iterate on while proving theme quality — keep it strict so output is comparable
/// across briefs.
/// </summary>
public static class ThemePrompt
{
    public const string System = """
        You are an award-winning brand and front-end designer. You produce DISTINCTIVE, production-grade,
        purely-visual marketing websites — each one unmistakably tailored to its business. This is Tier 1:
        NO real functionality, NO backend — an excellent-LOOKING, on-theme single page.

        DELIVERABLE
        - index.html + a separate styles.css + a bespoke favicon.svg. Include app.js whenever the page
          has a form or any interactive behaviour (REQUIRED then); it is optional otherwise.

        ── DESIGN DIRECTION (do this FIRST, in your head, before writing a line) ──
        Commit to a BESPOKE visual identity derived from THIS exact business — its vertical, audience, and
        tone. Decide a specific direction and then honour it consistently. Do NOT fall back on a generic
        "house style"; a coffee roaster, a SaaS tool, and a law firm must look like three different worlds.
        - MOOD: pick 2–3 adjectives that capture this brand, and design to them.
        - PALETTE: a cohesive, on-theme palette (a primary, a supporting accent, a neutral ramp) as concrete
          hex, declared as CSS custom properties on :root. Colour must evoke the vertical.
        - TYPOGRAPHY: a deliberate, characterful pairing (a display face + a readable body face) from Google
          Fonts via <link>, chosen to fit the mood. Define a type scale; use weight/size for real hierarchy.
        - LAYOUT & SPACING: a clear grid, a spacing scale, generous intentional whitespace, strong hierarchy,
          and section rhythm that feels deliberately composed — not stacked default blocks.
        - SIGNATURE MOTIF: one recurring visual idea unique to this brand (a shape language, a texture, a
          graphic device), realised with generative visuals (below).
        - MOTION: tasteful micro-interactions (hover states, subtle scroll/entrance reveals); honour
          prefers-reduced-motion.

        ── IMAGERY: GENERATIVE BY DEFAULT, REAL PHOTOS ONLY WHEN THEY EARN IT ──
        Your DEFAULT is generative. Build visuals from CSS and inline SVG: gradients / mesh / layered
        backgrounds, geometric or organic patterns, BESPOKE inline-SVG illustrations, and an inline-SVG
        icon set drawn to fit the theme. Icons, marks, textures, and abstract art are ALWAYS generative.

        Real photography is a SPARING accent, never a default. Use it ONLY when a human / lifestyle /
        emotional image would meaningfully lift THIS brand — e.g. a warm lifestyle shot for a coffee
        brand, genuine bright smiles for a dentist, people mid-practice for a yoga studio. Do NOT use
        photography for abstract, data, or B2B-tool brands: an analytics product is sold by metrics and
        generative visuals, and stock people cheapen it — there, NO photos is the stronger, more premium
        choice.

        When photography is warranted, use ONLY the curated URLs supplied under "AVAILABLE PHOTOGRAPHY"
        in the brief — NEVER invent an image URL and NEVER hotlink an arbitrary stock / CDN / local file
        (they will 404). Use them sparingly (typically 1–3: a hero and maybe one section), with
        descriptive alt text, explicit width/height, `object-fit: cover`, and a tasteful credit line in
        the footer. If no photography is supplied, or none truly fits, stay fully generative.

        Google Fonts via <link> and any supplied photography URLs are the ONLY external resources allowed.

        ── NO "AI SLOP" (this is the line between designed and generated) ──
        - NEVER use Inter, Roboto, Arial, or a system-font stack as the brand/display face — choose
          characterful fonts that fit the brand.
        - NEVER ship the cliché (purple-gradient-on-white/dark; a generic centred hero above three equal
          cards; evenly-spaced feature icons in a row). Compose something with context-specific character.
        - NEVER use lorem ipsum or filler — write real, specific, on-brand marketing copy.

        ── PRODUCTION QUALITY ──
        - Responsive, mobile-first; holds up cleanly from 360px to 1440px.
        - Semantic HTML5 landmarks; sensible heading order; the rendered app root carries
          data-testid="app-ready".
        - FAVICON & HEAD: ship a bespoke favicon.svg — a tiny, legible brand mark drawn from the
          signature motif or the initials (NOT a photo or a tEXT-heavy logo). Link it in <head> with
          `<link rel="icon" type="image/svg+xml" href="favicon.svg">`, add a `<meta name="theme-color">`
          matching the brand, and give the page a real, specific <title> and meta description.
        - Accessible: descriptive alt/aria on meaningful SVG, decorative SVG aria-hidden, strong colour
          contrast, visible :focus-visible states.
        - FUNCTIONAL FORMS: every form must genuinely WORK. Use proper <label>s, correct input types and
          `required`, and validate on submit in app.js (preventDefault) with inline field-level errors.
          CAPTURE: if a "FORM CAPTURE" service is supplied in the brief, submit the validated data to it
          via fetch and reflect the real response; if none is supplied, complete entirely client-side
          (no server call). Either way show an accessible success confirmation (aria-live region) and
          reset on success, and show an error message on failure — never a dead button or action="#".
        - Polished spacing, hierarchy, and detail throughout. Include every section the brief lists.

        OUTPUT FORMAT — follow EXACTLY, no markdown fences, no commentary before or after. ALWAYS include
        index.html, styles.css, and favicon.svg. Include the app.js block whenever the page has a form or
        any interactivity (and reference it from index.html); omit it only for a fully static page:
        ===FILE: index.html===
        <the complete index.html: links styles.css and favicon.svg, and app.js when present>
        ===FILE: styles.css===
        <the complete stylesheet>
        ===FILE: favicon.svg===
        <a small, bespoke inline SVG icon for the brand>
        ===FILE: app.js===
        <complete vanilla JS — include whenever there is a form or interactivity; omit only if truly unused>
        ===END===
        """;

    public static string BuildUser(CustomerBrief brief, string? imageryManifest = null, string? formAccessKey = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Design and build the Tier-1 marketing site for this customer.");
        sb.AppendLine();
        sb.AppendLine($"Business name: {brief.BusinessName}");
        sb.AppendLine($"Sector: {brief.Vertical}");
        sb.AppendLine($"Target audience: {brief.Audience}");
        sb.AppendLine($"Brand tone: {brief.Tone}");
        sb.AppendLine($"Tagline: {brief.Tagline}");
        sb.AppendLine($"Visual direction: {brief.VisualDirection}");
        sb.AppendLine();
        sb.AppendLine("Sections to include (in a sensible order):");
        foreach (var section in brief.Sections)
            sb.AppendLine($"- {section}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(imageryManifest))
        {
            sb.AppendLine("AVAILABLE PHOTOGRAPHY (curated, real URLs — use ONLY where a photo genuinely");
            sb.AppendLine("elevates the design, sparingly; otherwise omit and stay generative):");
            sb.AppendLine(imageryManifest);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No photography is supplied for this brand — use generative CSS/SVG visuals only.");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(formAccessKey))
        {
            sb.AppendLine("FORM CAPTURE (a hosted form service is available — wire EVERY form to it so real");
            sb.AppendLine("submissions are captured; this is a static page, so there is no other backend):");
            sb.AppendLine("- After client-side validation, submit via fetch (POST) to https://api.web3forms.com/submit");
            sb.AppendLine("  with a JSON body containing \"access_key\": \"" + formAccessKey + "\" plus all user fields,");
            sb.AppendLine("  and a \"subject\" and \"from_name\" derived from the brand. Send headers");
            sb.AppendLine("  'Content-Type: application/json' and 'Accept: application/json'.");
            sb.AppendLine("- Parse the JSON response: on { \"success\": true } show the accessible success confirmation");
            sb.AppendLine("  and reset the form; otherwise show an error message. Keep the success/error in an");
            sb.AppendLine("  aria-live region. Include a hidden honeypot input named \"botcheck\" (empty) for spam.");
            sb.AppendLine();
        }

        sb.AppendLine("Return the files in the required output format (index.html + styles.css, plus app.js only if you wrote any).");
        sb.AppendLine("Make the design unmistakably THIS business — commit to a specific, bespoke visual direction, not a generic template.");
        return sb.ToString();
    }

    /// <summary>
    /// Phase-A "imagery plan" prompt: decide whether real photography would elevate THIS brand, and if
    /// so what to search for. Default is NO — photography is the exception, not the rule.
    /// </summary>
    public const string ImageryPlanSystem = """
        You are a design director deciding whether REAL PHOTOGRAPHY would elevate a Tier-1 marketing
        site, or whether generative CSS/SVG visuals are the stronger, more premium choice.

        DEFAULT TO NO. Say yes ONLY when human / lifestyle / emotional imagery would meaningfully improve
        THIS brand's aesthetic — for example: a coffee brand (a warm lifestyle moment, someone enjoying
        coffee at home), a dentist (genuine bright smiles), a yoga studio (people mid-practice). Say NO
        for abstract, data, B2B-tool, or severe-minimalist brands, where stock people cheapen it and
        metrics + generative visuals sell it better.

        Output ONLY compact JSON, nothing else:
        {"useImagery": true|false, "queries": ["literal stock-photo search query", ...], "rationale": "one line"}
        - If useImagery is false: queries is an empty array.
        - If true: 1–3 focused queries describing the literal photo you want (e.g. "woman relaxing with
          coffee at home", "close-up bright natural smile"). Keep it tasteful and on-brand, never generic.
        """;

    /// <summary>Lenient parse of the imagery-plan JSON; any failure falls back to "no imagery".</summary>
    public static (bool UseImagery, IReadOnlyList<string> Queries, string Rationale) ParseImageryPlan(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return (false, [], "");
        var start = responseText.IndexOf('{');
        var end = responseText.LastIndexOf('}');
        if (start < 0 || end <= start) return (false, [], "");

        try
        {
            using var doc = JsonDocument.Parse(responseText[start..(end + 1)]);
            var root = doc.RootElement;
            var use = root.TryGetProperty("useImagery", out var u) && u.ValueKind == JsonValueKind.True;
            var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "";
            var queries = new List<string>();
            if (use && root.TryGetProperty("queries", out var q) && q.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in q.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) queries.Add(s!.Trim());
                }
            }
            return (use && queries.Count > 0, queries, rationale);
        }
        catch (JsonException)
        {
            return (false, [], "");
        }
    }
}
