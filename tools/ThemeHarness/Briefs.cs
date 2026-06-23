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

        new CustomerBrief(
            Slug: "solstice-sound",
            BusinessName: "Solstice Sound",
            Vertical: "Three-day outdoor electronic music festival",
            Audience: "Gen-Z and millennial ravers, festival regulars, and ticket-buying groups",
            Tone: "Loud, electric, maximalist, hedonistic, hyped",
            Tagline: "Three days. One horizon. No sleep.",
            VisualDirection: "Maximalist and high-energy: acid neon (hot magenta, electric lime, cyan) over " +
                "near-black, oversized condensed display type, diagonal/broken-grid layouts, CSS-animated " +
                "gradient meshes and noise, glitch and chromatic-aberration accents, bold inline-SVG poster " +
                "graphics. Should feel like a rave flyer — confident, overwhelming, unmissable.",
            Sections:
            [
                "Hero with the festival name, dates, and a pulsing 'Get tickets' call to action",
                "Lineup (headliners big, supporting acts in a dense grid)",
                "Stages & experiences",
                "Festival info (location, camping, times)",
                "Ticket tiers (early bird, general, VIP)",
                "Gallery / aftermovie teaser",
                "FAQ and socials in the footer",
            ]),

        new CustomerBrief(
            Slug: "atelier-noir",
            BusinessName: "Atelier Noir",
            Vertical: "Luxury architecture and interior-design studio",
            Audience: "Developers, private clients commissioning bespoke homes, and design press",
            Tone: "Quiet, minimal, confident, gallery-like, restrained",
            Tagline: "Space, considered.",
            VisualDirection: "Severe monochrome minimalism: near-black, white, and a single warm-grey neutral; " +
                "vast negative space; an editorial high-contrast serif paired with a precise grotesque sans; " +
                "thin hairline rules; a strict modular grid; the barest, slowest motion. Imagery is geometric " +
                "inline-SVG line work, never decorative. Should feel like a printed monograph — luxury by " +
                "restraint, not ornament.",
            Sections:
            [
                "Hero — a single confident statement and one restrained graphic mark",
                "Selected works (a quiet grid of projects with inline-SVG plan/line motifs)",
                "Studio philosophy",
                "Services (architecture, interiors, master planning)",
                "Recognition / press",
                "Contact and 'start a commission' in the footer",
            ]),

        // Faithful reproduction of the live Yorrixx-generated app `user-app-72c9e343`
        // (sport121_v20) for the model-tier quality investigation. Charter: a marketing
        // SPA to find specialist 1-2-1 sports coaches; display coaches with photo, name,
        // sports coached, and a clickable email link. No auth, no backend, no persistence.
        new CustomerBrief(
            Slug: "sport121",
            BusinessName: "Sport121",
            Vertical: "Marketing page for finding specialist 1-2-1 sports coaches",
            Audience: "Athletes looking for one-to-one specialist sports coaches",
            Tone: "Energetic, motivating, confident, athletic, professional",
            Tagline: "Find your specialist 1-2-1 sports coach.",
            VisualDirection: "Bold, energetic athletic styling: a confident accent colour, strong " +
                "display type paired with a clean humanist sans, motion that suggests movement and " +
                "momentum, and a card-based coach grid. Use real coach photography where it lifts the " +
                "design (each coach card shows a photo, name, the sports they coach, and a clear email " +
                "link). Modern, premium sports-brand feel — never a flat generic directory.",
            Sections:
            [
                "Hero with the value proposition and a 'Find your coach' call to action",
                "Coach grid — 8+ specialist coaches, each with photo, name, sports coached, and a clickable email link",
                "Why 1-2-1 coaching (benefits of personalised, specialist coaching)",
                "How it works (browse, email a coach, start training)",
                "Contact / get-in-touch and footer",
            ]),
    ];

    public static CustomerBrief? Find(string slug) =>
        All.FirstOrDefault(b => string.Equals(b.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
