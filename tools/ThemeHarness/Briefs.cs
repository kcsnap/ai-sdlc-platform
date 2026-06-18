namespace ThemeHarness;

/// <summary>
/// A single Tier-1 "customer requirement" — the input that drives one themed,
/// purely-visual marketing site. Deliberately spread across visual personalities
/// so the harness stress-tests theme variety, not just one happy path.
/// </summary>
public sealed record CustomerBrief(
    string Slug,
    string BusinessName,
    string Vertical,
    string Audience,
    string Tone,
    string Tagline,
    string VisualDirection,
    IReadOnlyList<string> Sections);

public static class Briefs
{
    public static readonly IReadOnlyList<CustomerBrief> All =
    [
        new CustomerBrief(
            Slug: "brightsmile-dental",
            BusinessName: "BrightSmile Dental",
            Vertical: "Family & orthodontic dental clinic",
            Audience: "Local families, nervous patients, and parents booking for their children",
            Tone: "Calm, reassuring, clean, trustworthy, professional",
            Tagline: "Gentle dentistry the whole family can smile about.",
            VisualDirection: "Soft clinical blues and warm whites, rounded corners, generous whitespace, " +
                "a friendly humanist sans-serif, subtle inline-SVG illustration, lots of air. " +
                "Should feel safe and modern, never sterile.",
            Sections:
            [
                "Hero with a clear 'Book an appointment' call to action",
                "Services (check-ups, orthodontics, teeth whitening, emergency care)",
                "Why choose us / trust signals (years established, gentle approach, modern equipment)",
                "Meet the team",
                "Patient testimonials",
                "FAQ (nervous patients, costs, children)",
                "Contact, location map placeholder, and opening hours in the footer",
            ]),

        new CustomerBrief(
            Slug: "ironbark-coffee",
            BusinessName: "Ironbark Coffee Roasters",
            Vertical: "Independent specialty coffee roaster and online bean shop",
            Audience: "Home-brewing enthusiasts, cafés sourcing beans, and gift buyers",
            Tone: "Warm, earthy, artisanal, hand-crafted, story-driven",
            Tagline: "Small-batch beans, roasted with patience.",
            VisualDirection: "Rich browns, cream, and burnt orange; kraft-paper texture via CSS; " +
                "a characterful serif display paired with a humanist sans; hand-drawn SVG accents. " +
                "Tactile, warm, and editorial — like the inside of a great roastery.",
            Sections:
            [
                "Hero with the brand story and an 'Explore our coffees' call to action",
                "Our coffees (single origins and house blends, with tasting notes)",
                "Our roasting process",
                "Subscription teaser (beans on your doorstep)",
                "Wholesale for cafés",
                "Visit the roastery",
                "Newsletter signup in the footer",
            ]),

        new CustomerBrief(
            Slug: "pulse-analytics",
            BusinessName: "Pulse Analytics",
            Vertical: "B2B SaaS product-analytics platform",
            Audience: "Product managers, growth teams, and data-driven founders",
            Tone: "Sharp, confident, modern, technical, trustworthy",
            Tagline: "See what your product is really doing.",
            VisualDirection: "Dark mode: deep slate / near-black background with an electric violet-to-cyan " +
                "accent gradient. Crisp geometric sans, dense grid, subtle glow, monospaced numerals, " +
                "and CSS/inline-SVG chart mockups. Feels like a precise, premium developer tool.",
            Sections:
            [
                "Hero with the product value proposition and an inline-SVG dashboard mockup",
                "Feature grid (funnels, retention, segmentation, real-time events)",
                "How it works (3 steps)",
                "Integrations strip",
                "Social proof (logos + a metric or two)",
                "Pricing teaser (3 tiers)",
                "Final call to action and footer",
            ]),

        new CustomerBrief(
            Slug: "hartwell-crane",
            BusinessName: "Hartwell & Crane",
            Vertical: "Boutique commercial law firm",
            Audience: "Business owners, in-house counsel, and high-net-worth clients",
            Tone: "Authoritative, established, discreet, refined",
            Tagline: "Counsel that protects what you've built.",
            VisualDirection: "Navy with warm gold accents on an ivory background; elegant serif headings; " +
                "classic typographic rhythm; formal whitespace; thin rules. Traditional and credible " +
                "without being stuffy or dated.",
            Sections:
            [
                "Hero with a confident statement of expertise",
                "Practice areas (corporate, disputes, real estate, employment)",
                "Our approach",
                "Notable matters / results",
                "Attorneys",
                "Insights / latest thinking",
                "Contact and 'request a consultation' in the footer",
            ]),

        new CustomerBrief(
            Slug: "tidal-yoga",
            BusinessName: "Tidal Yoga & Wellbeing",
            Vertical: "Boutique yoga and wellbeing studio",
            Audience: "Local wellness seekers, from complete beginners to experienced practitioners",
            Tone: "Serene, grounding, organic, inclusive, calm",
            Tagline: "Find your flow by the water's edge.",
            VisualDirection: "Muted sage, sand, and terracotta on off-white; soft organic shapes via inline " +
                "SVG 'blobs'; generous spacing; a calm humanist serif paired with a gentle sans. " +
                "Breathable, unhurried, and welcoming.",
            Sections:
            [
                "Hero with a calming gradient/SVG scene and a 'Book your first class' call to action",
                "Class types (vinyasa, restorative, beginners, prenatal)",
                "Schedule snapshot (a weekly grid)",
                "Membership and pricing teaser",
                "Meet the teachers",
                "The studio space",
                "Newsletter / first-class offer in the footer",
            ]),
    ];

    public static CustomerBrief? Find(string slug) =>
        All.FirstOrDefault(b => string.Equals(b.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
