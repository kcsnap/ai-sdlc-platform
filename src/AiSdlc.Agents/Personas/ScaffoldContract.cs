using System.Text;

namespace AiSdlc.Agents.Personas;

/// <summary>
/// Builds the Code Implementer's Scaffold Contract by COMPOSING per-capability fragments, rather than
/// selecting one of several near-duplicate monoliths. The FullStack contract is assembled from the
/// resolved capability axes — auth (Clerk) and persistence (Cosmos) — so an api-only app's contract
/// simply OMITS the Cosmos/RepositoryBase guidance instead of being contradicted by an override posture,
/// and the auth/no-auth duplication collapses into one place. Static is its own self-contained contract.
/// See docs/roadmap/fullstack-capability-derivation.md.
/// </summary>
internal static class ScaffoldContract
{
    /// <summary>Pick + assemble the contract for the resolved profile.</summary>
    public static string For(bool isStatic, bool needsAuth, bool hasDatabase) =>
        isStatic ? Static : BuildFullStack(needsAuth, hasDatabase);

    private static string BuildFullStack(bool needsAuth, bool hasDatabase)
    {
        var sections = new List<string>
        {
            Intro(needsAuth, hasDatabase),
            ImmutableFiles(needsAuth, hasDatabase),
            needsAuth ? AuthSection : NoAuthSection,
            WhereYouWrite(needsAuth, hasDatabase),
            DiSeam,
            Legal,
        };
        return string.Join("\n\n", sections);
    }

    private static string Intro(bool needsAuth, bool hasDatabase) =>
        "This app is built on a FIXED, pre-existing shell copied from the platform template. The shell " +
        "already COMPILES and already wires " + (needsAuth ? "authentication, layout" : "layout") +
        ", the API client," + (hasDatabase ? " the Cosmos client," : "") + " and the dependency-injection " +
        "seam." + (needsAuth ? "" : " THIS APP HAS NO AUTHENTICATION.") + " Do NOT author, modify, rename, " +
        "or recreate any shell file — they are immutable and will be discarded if you emit them.";

    private static string ImmutableFiles(bool needsAuth, bool hasDatabase)
    {
        var backend = "src/api/Program.cs,"
            + (needsAuth ? " src/api/Auth/** (ClerkJwtMiddleware, ClerkTokenValidator)," : "")
            + (hasDatabase ? " src/api/Data/CosmosClientFactory.cs," : "")
            + " src/api/Functions/HealthFunction.cs, src/api/host.json, src/api/Api.csproj";

        return "Immutable shell files (they already exist — import from them, never recreate them):\n" +
            "- Frontend: src/frontend/src/main.tsx, src/frontend/src/app/AppShell.tsx,\n" +
            "  src/frontend/src/lib/api.ts, src/frontend/src/vite-env.d.ts\n" +
            "- Backend: " + backend;
    }

    private const string AuthSection = """
        AUTHENTICATION IS ALREADY DONE — do not touch it. main.tsx wraps the app in <ClerkProvider>
        and app/AppShell.tsx renders the auth gate the immutable verification suite (auth.spec.ts)
        drives: signed-out shows the Clerk modal "Sign up" / "Sign in" buttons; signed-in renders a
        shell marked data-testid="signed-in". Never add another ClerkProvider, never build a custom
        LoginPage or email/password form, never use <RedirectToSignIn>. There is nothing for you to
        do for auth.
        """;

    private const string NoAuthSection = """
        THIS APP HAS NO AUTHENTICATION — there are no user accounts and no sign-in. main.tsx mounts
        <AppShell/> directly (NO <ClerkProvider>) and app/AppShell.tsx renders the app layout
        immediately, marked data-testid="app-ready". There is NO Clerk, NO src/api/Auth/, and NO
        tests/e2e/specs/auth.spec.ts. Never add ClerkProvider, a sign-in / sign-up flow, a LoginPage,
        an email/password form, <RedirectToSignIn>, the @clerk/clerk-react package, or any auth
        middleware; never gate a page on authentication; never read a signed-in user id on the backend.
        There is nothing for you to do for auth.
        """;

    private static string WhereYouWrite(bool needsAuth, bool hasDatabase)
    {
        var bullets = new List<string>
        {
            // Frontend routing — the shell renders AppRoutes inside the signed-in (auth) or app (no-auth) layout.
            "- Frontend routing: src/frontend/src/app/routes.tsx — export `AppRoutes`; the shell renders\n" +
            "  it inside the " + (needsAuth ? "signed-in" : "app") + " layout. Add react-router-dom here if you need client routing.",

            "- Frontend nav: src/frontend/src/app/nav.ts — supply the `navItems` array only; import the\n" +
            "  `NavItem` type from `./AppShell` (the shell owns it) — do NOT redefine it.",

            "- Frontend UI: build pages/components in src/frontend/src/features/** from the SHIPPED shadcn\n" +
            "  palette `@/components/ui/{button,card,checkbox,dialog,input,label,select,slider}`,\n" +
            "  `lucide-react` icons, and `react-router-dom` for routing. Do NOT add other UI libraries.",

            needsAuth
                ? "- Styling: src/frontend/src/theme.ts and/or Clerk `appearance` — data-driven only. Never\n  edit the shell components to restyle."
                : "- Styling: src/frontend/src/theme.ts — data-driven only. Never edit the shell components to\n  restyle.",

            "- API calls: import { apiUrl } from \"@/lib/api\" and use it. The client already reads the\n" +
            "  deployed API base URL. Never read import.meta.env directly and never create another client.",

            BackendFeatureCode(needsAuth, hasDatabase),
            hasDatabase ? DataAccess(needsAuth) : ApiOnlyDataAccess,
            HttpFunctions,
            Dependencies,
            Models,
            AcceptanceTests(needsAuth),
        };

        return "WHERE YOU WRITE:\n" + string.Join("\n", bullets);
    }

    private static string BackendFeatureCode(bool needsAuth, bool hasDatabase)
    {
        const string head =
            "- Backend feature code: src/api/Features/**, src/api/Models/**, src/api/Services/** (all\n" +
            "  AI-owned).";
        if (!hasDatabase)
            return head + " There is no datastore — keep feature services stateless.";

        return head + " The sample `items` feature (Data/CosmosItemStore.cs, Functions/ItemsFunction.cs)\n" +
            "  may be replaced" + (needsAuth ? "." : "; it is NOT scoped to a user (there is no auth).");
    }

    private static string DataAccess(bool needsAuth) =>
        "- Data access — extend `Api.Data.RepositoryBase<T>` (one subclass per document type; inject the\n" +
        "  registered `CosmosClient`; `T` must carry a string `id`, which is the partition key). See the\n" +
        "  worked example `CosmosItemStore`. Do NOT open a raw `Container` yourself, do NOT take a\n" +
        "  `Container` constructor parameter, and do NOT add a second database/container. Cosmos types:\n" +
        "  `using Microsoft.Azure.Cosmos;` (NOT `Azure.Cosmos`); the container type is `Container` (NOT\n" +
        "  `CosmosContainer`); catch `CosmosException`. Reference shell types via their namespace:\n" +
        "  " + (needsAuth ? "`using Api.Data;`, `using Api.Auth;`" : "`using Api.Data;`") +
        ". Declare each model/record ONCE across the codebase.";

    private const string ApiOnlyDataAccess =
        "- Data access — this app is API-ONLY: there is NO database and NO Cosmos. Do NOT extend\n" +
        "  `RepositoryBase`, inject `CosmosClient`, or add any datastore. Keep features stateless —\n" +
        "  compute, validate, call any allowed integration, and return results; never persist data\n" +
        "  between requests.";

    private const string HttpFunctions =
        "- HTTP functions — follow the sample `HealthFunction`: `[Function(\"Name\")]` +\n" +
        "  `[HttpTrigger(...)] HttpRequest req` returning `IActionResult`, with\n" +
        "  `using Microsoft.AspNetCore.Mvc;` and `using Microsoft.AspNetCore.Http;`. For configuration\n" +
        "  use `IConfiguration` (`using Microsoft.Extensions.Configuration;`) or\n" +
        "  `Environment.GetEnvironmentVariable(...)`.";

    private const string Dependencies =
        "- Dependencies — `Api.csproj` is IMMUTABLE: you CANNOT add NuGet packages. Use only the .NET\n" +
        "  base class library and the packages the sample shell code already uses (Microsoft.Azure.Cosmos,\n" +
        "  Microsoft.Azure.Functions.Worker, Microsoft.AspNetCore.Mvc/Http, Microsoft.Extensions.*).\n" +
        "  Never `using` a third-party SDK that the shell does not already use. EMAIL is provided —\n" +
        "  inject the shipped `Api.Email.IEmailSender` (`Task SendAsync(string to, string subject,\n" +
        "  string htmlBody, CancellationToken)`); it is wired to SendGrid (SENDGRID_API_KEY is\n" +
        "  provisioned), so do NOT stub email or instantiate SendGrid yourself. For OTHER external\n" +
        "  services (SMS, payments, a third-party API) that no referenced package provides, define an\n" +
        "  interface and a no-op/logging stub so the app COMPILES — wiring the real provider is a later,\n" +
        "  human step. The same applies on the frontend: only add a package.json dependency you actually\n" +
        "  import, and prefer the shipped libraries.";

    private const string Models =
        "- Models & self-consistency: feature models are MUTABLE POCOs — `public T Prop { get; set; }`,\n" +
        "  NOT init-only properties or positional records — so an entity read from the store can be\n" +
        "  updated by assignment (`coach.Name = x`) and saved. Generate code that agrees with itself:\n" +
        "  only call methods and properties you actually define, use the SAME class and method names in\n" +
        "  every file that references them, and match argument types. The API builds with\n" +
        "  TreatWarningsAsErrors, so a call to an undefined method, an init-only mutation (CS8852), or a\n" +
        "  type mismatch fails the build.";

    private static string AcceptanceTests(bool needsAuth) =>
        "- Acceptance tests: author tests/e2e/specs/acceptance.spec.ts over the seeded throwing stubs —\n" +
        "  replace each stub body with a real Playwright test for that acceptance criterion (the\n" +
        "  criteria are the contract). Never delete or skip a test. This is the ONE file under\n" +
        "  tests/e2e/ you write; everything else there is immutable." +
        (needsAuth ? "" : " There is no auth, so do NOT add\n" +
        "  sign-in or registration steps — the app loads straight to content (data-testid=\"app-ready\").");

    private const string DiSeam = """
        BACKEND DI SEAM — HARD CONTRACT. Register every feature service in
        src/api/Features/FeatureRegistration.cs by editing the body of `AddFeatures`. You MUST keep
        the exact namespace `Api.Features`, the class `FeatureRegistration`, and the signature
        `public static void AddFeatures(IServiceCollection services)`. The immutable Program.cs calls
        it; renaming or removing it breaks the build.
        """;

    private const string Legal = """
        LEGAL: a footer linking the Privacy Policy and Terms of Service already exists in the shell
        layout, and the platform provides those pages. Do NOT add legal links and do NOT author legal
        pages or prose.
        """;

    // Static profile (Stack profile: Static) — hand-written HTML/CSS(+JS), seeded from the static
    // template; the FullStack contract would actively mislead it. Self-contained (no shared fragments).
    public const string Static = """
        This app is a STATIC site (Stack profile: Static), seeded from the static template. There is NO
        React, NO Vite/npm build, NO C# API / Azure Functions, NO Cosmos or any database, and NO
        authentication. The deploy serves static files only — anything that needs a build or a server
        cannot run.

        YOU AUTHOR (these ARE the app — write real, topic-relevant content, no placeholder text):
        - the page: `index.html` (semantic HTML5; the rendered root carries `data-testid="app-ready"`).
          Give it a real, specific <title>, a meta description, and a <meta name="theme-color"> that
          matches the brand.
        - the styles: `styles.css` (modern CSS; no framework needed),
        - a bespoke `favicon.svg` — a small, legible brand mark drawn from the design's signature motif
          or the initials (NOT a photo), linked in <head> with
          `<link rel="icon" type="image/svg+xml" href="favicon.svg">`.
        - `app.js` (plain, `<script>`-loadable vanilla JavaScript) where it improves UX — e.g. a
          client-side filter over hard-coded data, or form handling (below). No bundler, no npm packages.
        - Hard-code any fixed data (e.g. a list of items) directly into the page. Do NOT call your own
          backend or persist anything — there is none.
        - FUNCTIONAL FORMS: any form must genuinely WORK. Use real <label>s, correct input types and
          `required`, and validate on submit in app.js (preventDefault) with inline field errors and an
          accessible success confirmation (aria-live region), then reset. A static page has no backend,
          so by DEFAULT complete client-side (no server call) — never a dead button or an action="#"
          no-op. If a "Form Capture" service is supplied in your context, submit the validated data to
          it instead (and reflect its response); otherwise stay client-side.
        - Acceptance tests: you may fill `tests/e2e/specs/acceptance.spec.ts` over its seeded stubs with
          real RENDER-ONLY assertions (content present, internal links resolve, a form shows its success
          confirmation, no scaffold text). NEVER assert against `/api/*`, a database, or a live form POST.

        IMMUTABLE — do NOT author, modify, or recreate (machine-managed): `.github/workflows/**` (the
        static deploy + verify workflows) and the rest of `tests/e2e/**` (the render-only harness).

        Do NOT add React, Vite, a `package.json` build, a C# project, Azure Functions, Cosmos, a `fetch`
        to your own backend, or auth — there is nowhere to host them. (Submitting a form to the supplied
        Form Capture service is the one allowed outbound call.)
        """;
}
