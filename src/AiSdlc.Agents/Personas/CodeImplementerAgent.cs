using System.Text;
using System.Text.RegularExpressions;
using AiSdlc.ModelProviders;
using AiSdlc.Shared;

namespace AiSdlc.Agents.Personas;

/// <summary>One planned file in the implementation manifest.</summary>
public sealed record ManifestItem(string Path, string Purpose);

/// <summary>
/// Generates the implementation in chunks: a file manifest first, then the files in small
/// batches, each within its own model response. Single-shot generation truncated silently on
/// large apps — files referenced by the entrypoint were never emitted, and review failed the
/// run for incompleteness (issue #76). A manifest makes "referenced but missing" detectable:
/// files absent after a recovery pass fail the stage instead of committing a partial app.
/// </summary>
public sealed class CodeImplementerAgent : IAgent
{
    // Small enough that a batch of complete files fits comfortably in one response.
    internal const int BatchSize = 3;

    // Hard stop against a runaway manifest; the prompt steers well below this.
    internal const int ManifestFileCap = 60;

    private const string ManifestSystemPrompt = """
        You are the Code Implementer planning stage in an AI-driven SDLC pipeline.

        Plan the set of FEATURE files needed to implement the request, on top of the fixed shell
        described in the Scaffold Contract. The shell already exists and compiles.

        Rules:
        - Output ONLY a manifest block — no prose, no explanation, nothing outside it:

          <manifest>
          <item path="relative/path/from/repo/root">one-line purpose of the file</item>
          </manifest>

        - Plan ONLY feature files. NEVER list a shell/infra file — they already exist and any you
          list will be discarded. Do NOT list: src/frontend/src/main.tsx, app/AppShell.tsx,
          lib/api.ts, vite-env.d.ts; src/api/Program.cs, src/api/Auth/**,
          src/api/Data/CosmosClientFactory.cs, src/api/Functions/HealthFunction.cs, host.json,
          Api.csproj; anything under .github/, and anything under tests/e2e/ EXCEPT
          tests/e2e/specs/acceptance.spec.ts.
        - DO list the feature slots you fill: src/frontend/src/app/routes.tsx,
          src/frontend/src/app/nav.ts, src/frontend/src/theme.ts, src/frontend/src/features/**,
          src/api/Features/** including src/api/Features/FeatureRegistration.cs, and
          tests/e2e/specs/acceptance.spec.ts (author it over the seeded throwing stubs).
        - List every feature file required for a complete, runnable implementation — if an import
          will reference a file, that file MUST be in the manifest.
        - Keep the design modular: prefer many focused files over few large ones.
        """;

    private const string BatchSystemPrompt = """
        You are the Code Implementer in an AI-driven SDLC pipeline.

        The implementation plan is already fixed. You will be told exactly which files to
        generate in this batch — generate those files COMPLETELY, consistent with the plan.

        Rules:
        - Output ONLY file blocks — no prose, no explanation, no text outside blocks.
        - For every file, use EXACTLY this format:

          <file path="relative/path/from/repo/root">
          (file content here)
          </file>

        - Paths are relative to the repository root (e.g. README.md, src/api/Controllers/Foo.cs).
        - Generate ONLY the files requested for this batch, each one complete — never truncate
          or stub a file with placeholders.
        - The literal text </file> must never appear inside file content.
        """;

    private const string SingleShotSystemPrompt = """
        You are the Code Implementer in an AI-driven SDLC pipeline.

        Write the actual files needed to implement the feature based on the brief,
        business analysis, architecture review, and implementation specification provided.

        Rules:
        - Output ONLY file blocks — no prose, no explanation, no text outside blocks.
        - For every file to create or modify, use EXACTLY this format:

          <file path="relative/path/from/repo/root">
          (file content here)
          </file>

        - Paths are relative to the repository root (e.g. README.md, src/api/Controllers/Foo.cs).
        - Output all files required to fully implement the feature.
        - Do not output anything outside the file blocks.
        - The literal text </file> must never appear inside file content.
        """;

    private const string RepairSystemPrompt = """
        You are the Code Implementer in REPAIR mode in an AI-driven SDLC pipeline.

        The application already exists but failed verification — either downstream
        verification after release, or the pull request's CI build. You are given the
        CURRENT source code and the findings (often exact compiler output). Your job is
        a surgical fix, not a rewrite.

        The application ALREADY BUILDS and is merged to main — only the listed findings are
        wrong. This is edit mode, not greenfield: never regenerate the app, and never re-emit
        a file the findings do not implicate.

        Rules:
        - Fix ONLY what the findings implicate. Do not redesign, restructure, rename, or
          "improve" anything else — unchanged files must not be touched.
        - NEVER rename namespaces, classes, files, or folders — a rename is a refactor,
          not a repair, and it breaks every other file that references the old name.
        - NEVER create new files unless an error explicitly requires one (e.g. a missing
          module the findings name).
        - NEVER touch the immutable shell or platform files (see the Scaffold Contract): anything
          under .github/ or tests/e2e/ (except tests/e2e/specs/acceptance.spec.ts), and the app
          shell — main.tsx, app/AppShell.tsx, lib/api.ts, vite-env.d.ts, src/api/Program.cs,
          src/api/Auth/**, Data/CosmosClientFactory.cs, Functions/HealthFunction.cs, host.json,
          Api.csproj. They are read-only.
        - Output ONLY the files that need to change, each as a complete file using EXACTLY:

          <file path="relative/path/from/repo/root">
          (complete fixed file content)
          </file>

        - Each output file must be the COMPLETE corrected file, based on the existing
          content shown to you — never a fragment or diff.
        - Output nothing outside the file blocks.
        - The literal text </file> must never appear inside file content.
        """;

    // Scaffold-first (#131): every user-app is created by copying the template repo
    // (kcsnap/ai-sdlc-react-dotnet-template), so a tested, compiling shell already exists — auth,
    // layout, the API client, the Cosmos client, and the DI seam are all wired. The Code
    // Implementer's job is to FILL FEATURE SLOTS, never to re-author the shell. This contract
    // replaces the old "describe how to build auth" doc, which still produced wrong imports
    // (@clerk/react) and RedirectToSignIn in the v004 baseline. It travels into every
    // generation/repair prompt and is enforced by IsProtectedPath on the orchestrator side.
    internal const string ScaffoldContractLabel = "Scaffold Contract (DO NOT BREAK)";

    // Auth variant (Charter.Constraints.NeedsAuth == true) — today's contract, unchanged. The no-auth
    // variant below (NeedsAuth == false) shares most guidance; keep the non-auth parts in sync when
    // either changes. Selected in BuildContextDocs. See docs/roadmap/conditional-auth-yorrixx-brief.md.
    internal const string ScaffoldContractDocAuth = """
        This app is built on a FIXED, pre-existing shell copied from the platform template. The
        shell already COMPILES and already wires authentication, layout, the API client, the Cosmos
        client, and the dependency-injection seam. Do NOT author, modify, rename, or recreate any
        shell file — they are immutable and will be discarded if you emit them.

        Immutable shell files (they already exist — import from them, never recreate them):
        - Frontend: src/frontend/src/main.tsx, src/frontend/src/app/AppShell.tsx,
          src/frontend/src/lib/api.ts, src/frontend/src/vite-env.d.ts
        - Backend: src/api/Program.cs, src/api/Auth/** (ClerkJwtMiddleware, ClerkTokenValidator),
          src/api/Data/CosmosClientFactory.cs, src/api/Functions/HealthFunction.cs,
          src/api/host.json, src/api/Api.csproj

        AUTHENTICATION IS ALREADY DONE — do not touch it. main.tsx wraps the app in <ClerkProvider>
        and app/AppShell.tsx renders the auth gate the immutable verification suite (auth.spec.ts)
        drives: signed-out shows the Clerk modal "Sign up" / "Sign in" buttons; signed-in renders a
        shell marked data-testid="signed-in". Never add another ClerkProvider, never build a custom
        LoginPage or email/password form, never use <RedirectToSignIn>. There is nothing for you to
        do for auth.

        WHERE YOU WRITE:
        - Frontend routing: src/frontend/src/app/routes.tsx — export `AppRoutes`; the shell renders
          it inside the signed-in layout. Add react-router-dom here if you need client routing.
        - Frontend nav: src/frontend/src/app/nav.ts — supply the `navItems` array only; import the
          `NavItem` type from `./AppShell` (the shell owns it) — do NOT redefine it.
        - Frontend UI: build pages/components in src/frontend/src/features/** from the SHIPPED shadcn
          palette `@/components/ui/{button,card,checkbox,dialog,input,label,select,slider}`,
          `lucide-react` icons, and `react-router-dom` for routing. Do NOT add other UI libraries.
        - Styling: src/frontend/src/theme.ts and/or Clerk `appearance` — data-driven only. Never
          edit the shell components to restyle.
        - API calls: import { apiUrl } from "@/lib/api" and use it. The client already reads the
          deployed API base URL. Never read import.meta.env directly and never create another client.
        - Backend feature code: src/api/Features/**, src/api/Models/**, src/api/Services/** (all
          AI-owned). The sample `items` feature (Data/CosmosItemStore.cs, Functions/ItemsFunction.cs)
          may be replaced.
        - Data access — extend `Api.Data.RepositoryBase<T>` (one subclass per document type; inject the
          registered `CosmosClient`; `T` must carry a string `id`, which is the partition key). See the
          worked example `CosmosItemStore`. Do NOT open a raw `Container` yourself, do NOT take a
          `Container` constructor parameter, and do NOT add a second database/container. Cosmos types:
          `using Microsoft.Azure.Cosmos;` (NOT `Azure.Cosmos`); the container type is `Container` (NOT
          `CosmosContainer`); catch `CosmosException`. Reference shell types via their namespace:
          `using Api.Data;`, `using Api.Auth;`. Declare each model/record ONCE across the codebase.
        - HTTP functions — follow the sample `HealthFunction`: `[Function("Name")]` +
          `[HttpTrigger(...)] HttpRequest req` returning `IActionResult`, with
          `using Microsoft.AspNetCore.Mvc;` and `using Microsoft.AspNetCore.Http;`. For configuration
          use `IConfiguration` (`using Microsoft.Extensions.Configuration;`) or
          `Environment.GetEnvironmentVariable(...)`.
        - Dependencies — `Api.csproj` is IMMUTABLE: you CANNOT add NuGet packages. Use only the .NET
          base class library and the packages the sample shell code already uses (Microsoft.Azure.Cosmos,
          Microsoft.Azure.Functions.Worker, Microsoft.AspNetCore.Mvc/Http, Microsoft.Extensions.*).
          Never `using` a third-party SDK that the shell does not already use. EMAIL is provided —
          inject the shipped `Api.Email.IEmailSender` (`Task SendAsync(string to, string subject,
          string htmlBody, CancellationToken)`); it is wired to SendGrid (SENDGRID_API_KEY is
          provisioned), so do NOT stub email or instantiate SendGrid yourself. For OTHER external
          services (SMS, payments, a third-party API) that no referenced package provides, define an
          interface and a no-op/logging stub so the app COMPILES — wiring the real provider is a later,
          human step. The same applies on the frontend: only add a package.json dependency you actually
          import, and prefer the shipped libraries.
        - Models & self-consistency: feature models are MUTABLE POCOs — `public T Prop { get; set; }`,
          NOT init-only properties or positional records — so an entity read from the store can be
          updated by assignment (`coach.Name = x`) and saved. Generate code that agrees with itself:
          only call methods and properties you actually define, use the SAME class and method names in
          every file that references them, and match argument types. The API builds with
          TreatWarningsAsErrors, so a call to an undefined method, an init-only mutation (CS8852), or a
          type mismatch fails the build.
        - Acceptance tests: author tests/e2e/specs/acceptance.spec.ts over the seeded throwing stubs —
          replace each stub body with a real Playwright test for that acceptance criterion (the
          criteria are the contract). Never delete or skip a test. This is the ONE file under
          tests/e2e/ you write; everything else there is immutable.

        BACKEND DI SEAM — HARD CONTRACT. Register every feature service in
        src/api/Features/FeatureRegistration.cs by editing the body of `AddFeatures`. You MUST keep
        the exact namespace `Api.Features`, the class `FeatureRegistration`, and the signature
        `public static void AddFeatures(IServiceCollection services)`. The immutable Program.cs calls
        it; renaming or removing it breaks the build.

        LEGAL: a footer linking the Privacy Policy and Terms of Service already exists in the shell
        layout, and the platform provides those pages. Do NOT add legal links and do NOT author legal
        pages or prose.
        """;

    // No-auth variant (Charter.Constraints.NeedsAuth == false): Yorrixx seeds a shell with NO Clerk,
    // NO src/api/Auth/, and NO auth.spec.ts (conditional-auth-yorrixx-brief.md). The contract must NOT
    // claim auth is wired — that mismatch is exactly the bug this fixes. Shares the auth-agnostic tail
    // (data access, deps, models, DI seam, legal) with the auth doc above; keep those in sync.
    internal const string ScaffoldContractDocNoAuth = """
        This app is built on a FIXED, pre-existing shell copied from the platform template. The
        shell already COMPILES and already wires layout, the API client, the Cosmos client, and the
        dependency-injection seam. THIS APP HAS NO AUTHENTICATION. Do NOT author, modify, rename, or
        recreate any shell file — they are immutable and will be discarded if you emit them.

        Immutable shell files (they already exist — import from them, never recreate them):
        - Frontend: src/frontend/src/main.tsx, src/frontend/src/app/AppShell.tsx,
          src/frontend/src/lib/api.ts, src/frontend/src/vite-env.d.ts
        - Backend: src/api/Program.cs, src/api/Data/CosmosClientFactory.cs,
          src/api/Functions/HealthFunction.cs, src/api/host.json, src/api/Api.csproj

        THIS APP HAS NO AUTHENTICATION — there are no user accounts and no sign-in. main.tsx mounts
        <AppShell/> directly (NO <ClerkProvider>) and app/AppShell.tsx renders the app layout
        immediately, marked data-testid="app-ready". There is NO Clerk, NO src/api/Auth/, and NO
        tests/e2e/specs/auth.spec.ts. Never add ClerkProvider, a sign-in / sign-up flow, a LoginPage,
        an email/password form, <RedirectToSignIn>, the @clerk/clerk-react package, or any auth
        middleware; never gate a page on authentication; never read a signed-in user id on the backend.
        There is nothing for you to do for auth.

        WHERE YOU WRITE:
        - Frontend routing: src/frontend/src/app/routes.tsx — export `AppRoutes`; the shell renders
          it inside the app layout. Add react-router-dom here if you need client routing.
        - Frontend nav: src/frontend/src/app/nav.ts — supply the `navItems` array only; import the
          `NavItem` type from `./AppShell` (the shell owns it) — do NOT redefine it.
        - Frontend UI: build pages/components in src/frontend/src/features/** from the SHIPPED shadcn
          palette `@/components/ui/{button,card,checkbox,dialog,input,label,select,slider}`,
          `lucide-react` icons, and `react-router-dom` for routing. Do NOT add other UI libraries.
        - Styling: src/frontend/src/theme.ts — data-driven only. Never edit the shell components to
          restyle.
        - API calls: import { apiUrl } from "@/lib/api" and use it. The client already reads the
          deployed API base URL. Never read import.meta.env directly and never create another client.
        - Backend feature code: src/api/Features/**, src/api/Models/**, src/api/Services/** (all
          AI-owned). The sample `items` feature (Data/CosmosItemStore.cs, Functions/ItemsFunction.cs)
          may be replaced; it is NOT scoped to a user (there is no auth).
        - Data access — extend `Api.Data.RepositoryBase<T>` (one subclass per document type; inject the
          registered `CosmosClient`; `T` must carry a string `id`, which is the partition key). See the
          worked example `CosmosItemStore`. Do NOT open a raw `Container` yourself, do NOT take a
          `Container` constructor parameter, and do NOT add a second database/container. Cosmos types:
          `using Microsoft.Azure.Cosmos;` (NOT `Azure.Cosmos`); the container type is `Container` (NOT
          `CosmosContainer`); catch `CosmosException`. Reference shell types via their namespace:
          `using Api.Data;`. Declare each model/record ONCE across the codebase.
        - HTTP functions — follow the sample `HealthFunction`: `[Function("Name")]` +
          `[HttpTrigger(...)] HttpRequest req` returning `IActionResult`, with
          `using Microsoft.AspNetCore.Mvc;` and `using Microsoft.AspNetCore.Http;`. For configuration
          use `IConfiguration` (`using Microsoft.Extensions.Configuration;`) or
          `Environment.GetEnvironmentVariable(...)`.
        - Dependencies — `Api.csproj` is IMMUTABLE: you CANNOT add NuGet packages. Use only the .NET
          base class library and the packages the sample shell code already uses (Microsoft.Azure.Cosmos,
          Microsoft.Azure.Functions.Worker, Microsoft.AspNetCore.Mvc/Http, Microsoft.Extensions.*).
          Never `using` a third-party SDK that the shell does not already use. EMAIL is provided —
          inject the shipped `Api.Email.IEmailSender` (`Task SendAsync(string to, string subject,
          string htmlBody, CancellationToken)`); it is wired to SendGrid (SENDGRID_API_KEY is
          provisioned), so do NOT stub email or instantiate SendGrid yourself. For OTHER external
          services (SMS, payments, a third-party API) that no referenced package provides, define an
          interface and a no-op/logging stub so the app COMPILES — wiring the real provider is a later,
          human step. The same applies on the frontend: only add a package.json dependency you actually
          import, and prefer the shipped libraries.
        - Models & self-consistency: feature models are MUTABLE POCOs — `public T Prop { get; set; }`,
          NOT init-only properties or positional records — so an entity read from the store can be
          updated by assignment (`coach.Name = x`) and saved. Generate code that agrees with itself:
          only call methods and properties you actually define, use the SAME class and method names in
          every file that references them, and match argument types. The API builds with
          TreatWarningsAsErrors, so a call to an undefined method, an init-only mutation (CS8852), or a
          type mismatch fails the build.
        - Acceptance tests: author tests/e2e/specs/acceptance.spec.ts over the seeded throwing stubs —
          replace each stub body with a real Playwright test for that acceptance criterion (the
          criteria are the contract). Never delete or skip a test. This is the ONE file under
          tests/e2e/ you write; everything else there is immutable. There is no auth, so do NOT add
          sign-in or registration steps — the app loads straight to content (data-testid="app-ready").

        BACKEND DI SEAM — HARD CONTRACT. Register every feature service in
        src/api/Features/FeatureRegistration.cs by editing the body of `AddFeatures`. You MUST keep
        the exact namespace `Api.Features`, the class `FeatureRegistration`, and the signature
        `public static void AddFeatures(IServiceCollection services)`. The immutable Program.cs calls
        it; renaming or removing it breaks the build.

        LEGAL: a footer linking the Privacy Policy and Terms of Service already exists in the shell
        layout, and the platform provides those pages. Do NOT add legal links and do NOT author legal
        pages or prose.
        """;

    private static string ScaffoldContractDocFor(bool needsAuth) =>
        needsAuth ? ScaffoldContractDocAuth : ScaffoldContractDocNoAuth;

    private const string RetryPrompt =
        "Your previous response contained no `<file path=\"...\">` blocks. " +
        "You MUST wrap every file in `<file path=\"...\">` tags. " +
        "Output ONLY file blocks — nothing else.";

    private static readonly Regex ManifestItemRegex = new(
        @"<item path=""([^""]+)"">\s*([^<]*?)\s*</item>",
        RegexOptions.Compiled);

    private readonly IModelProvider _model;

    public CodeImplementerAgent(IModelProvider model) => _model = model;

    public string Name => AgentNames.CodeImplementer;

    public async Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextDocs = BuildContextDocs(request.Context);
        AgentContextDocuments.AddStandard(contextDocs, request.Context);
        var userPrompt  = BuildUserPrompt(request.Context);

        // Repair mode: the app exists and failed verification (downstream reopen) or its
        // PR's CI build (in-run) — iterate on the existing code with the findings, never
        // regenerate. Regeneration cannot converge: each rewrite introduces fresh defects
        // in different files (#92, #95).
        if (IsRepairRequest(request.Context))
        {
            return await RepairAsync(contextDocs, userPrompt, request.Context.IssueNumber, cancellationToken);
        }

        var manifestResponse = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeImplementationManifest",
            SystemPrompt     = ManifestSystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 2000
        }, cancellationToken);

        var manifest = ParseManifest(manifestResponse.ResponseText);

        // No parseable manifest (model ignored the format, or a fake/test provider) —
        // fall back to the original single-shot generation.
        if (manifest.Count == 0)
            return await SingleShotAsync(contextDocs, userPrompt, request.Context.IssueNumber, cancellationToken);

        if (manifest.Count > ManifestFileCap)
            throw new InvalidOperationException(
                $"Implementation manifest listed {manifest.Count} files (cap {ManifestFileCap}) — " +
                "refusing runaway generation. The implementation specification likely needs splitting.");

        var manifestText = string.Join("\n", manifest.Select(m => $"- {m.Path} — {m.Purpose}"));
        var emitted      = new Dictionary<string, FileChange>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in manifest.Chunk(BatchSize))
            await GenerateBatchAsync(batch, manifestText, contextDocs, userPrompt, emitted, cancellationToken);

        // Recovery pass: anything missing (skipped, or its block truncated mid-file and thus
        // unparseable) is retried one file at a time, the most truncation-resistant shape.
        foreach (var item in manifest.Where(m => !emitted.ContainsKey(m.Path)).ToArray())
            await GenerateBatchAsync([item], manifestText, contextDocs, userPrompt, emitted, cancellationToken);

        var missing = manifest.Where(m => !emitted.ContainsKey(m.Path)).Select(m => m.Path).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Code implementation incomplete after recovery pass — missing files: {string.Join(", ", missing)}. " +
                "Failing the stage rather than committing a partial implementation.");

        var output = new StringBuilder();
        foreach (var change in OrderEmitted(manifest, emitted))
        {
            output.AppendLine($"<file path=\"{change.Path}\">");
            output.AppendLine(change.Content);
            output.AppendLine("</file>");
            output.AppendLine();
        }

        return new AgentResult
        {
            AgentName      = Name,
            Status         = "Completed",
            Summary        = $"Code implementation generated for issue #{request.Context.IssueNumber} " +
                             $"({emitted.Count} files from a {manifest.Count}-file manifest).",
            OutputMarkdown = output.ToString()
        };
    }

    internal static bool IsRepairRequest(AgentContext ctx) =>
        (!string.IsNullOrWhiteSpace(GetMeta(ctx, "reopenFindings")) ||
         !string.IsNullOrWhiteSpace(GetMeta(ctx, "ciFindings")))
        && !string.IsNullOrWhiteSpace(GetMeta(ctx, "existingSource"));

    internal static IReadOnlyList<ManifestItem> ParseManifest(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return [];
        return ManifestItemRegex.Matches(responseText)
            .Select(m => new ManifestItem(m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()))
            .DistinctBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task GenerateBatchAsync(
        IReadOnlyList<ManifestItem> batch, string manifestText,
        Dictionary<string, string> contextDocs, string userPrompt,
        Dictionary<string, FileChange> emitted, CancellationToken cancellationToken)
    {
        var alreadyGenerated = emitted.Count == 0
            ? "(none yet)"
            : string.Join("\n", emitted.Keys.Select(p => $"- {p}"));

        var batchPrompt =
            $"""
            {userPrompt}

            The complete implementation plan (fixed — do not redesign it):
            {manifestText}

            Files already generated in earlier batches (do not regenerate):
            {alreadyGenerated}

            Generate ONLY these files now, each one complete:
            {string.Join("\n", batch.Select(m => $"- {m.Path} — {m.Purpose}"))}
            """;

        var response = await _model.CompleteAsync(new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeImplementation",
            SystemPrompt     = BatchSystemPrompt,
            UserPrompt       = batchPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 8000
        }, cancellationToken);

        // Only complete blocks parse (the regex requires the closing tag), so a truncated
        // trailing file is naturally excluded and lands in the recovery pass.
        foreach (var change in CodeChangeParser.Parse(response.ResponseText))
            emitted[change.Path] = change;
    }

    private async Task<AgentResult> RepairAsync(
        Dictionary<string, string> contextDocs, string userPrompt, int issueNumber,
        CancellationToken cancellationToken)
    {
        var modelRequest = new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeRepair",
            SystemPrompt     = RepairSystemPrompt,
            UserPrompt       = userPrompt +
                "\n\nApply the minimal fix for the findings document provided (Verification Findings " +
                "or CI Failure Findings) against the Existing Source. Output ONLY the corrected files.",
            ContextDocuments = contextDocs,
            MaxTokens        = 8000
        };

        var response = await _model.CompleteAsync(modelRequest, cancellationToken);
        if (!response.ResponseText.Contains("<file ", StringComparison.Ordinal))
        {
            response = await _model.CompleteAsync(modelRequest with
            {
                UserPrompt = $"{modelRequest.UserPrompt}\n\n{RetryPrompt}"
            }, cancellationToken);
        }

        var fileCount = CodeChangeParser.Parse(response.ResponseText).Count;
        return new AgentResult
        {
            AgentName      = Name,
            Status         = "Completed",
            Summary        = $"Repaired issue #{issueNumber}: minimal fix touching {fileCount} file(s) for the verification findings.",
            OutputMarkdown = response.ResponseText
        };
    }

    private async Task<AgentResult> SingleShotAsync(
        Dictionary<string, string> contextDocs, string userPrompt, int issueNumber,
        CancellationToken cancellationToken)
    {
        var modelRequest = new ModelRequest
        {
            AgentName        = Name,
            TaskType         = "CodeImplementation",
            SystemPrompt     = SingleShotSystemPrompt,
            UserPrompt       = userPrompt,
            ContextDocuments = contextDocs,
            MaxTokens        = 8000
        };

        var response = await _model.CompleteAsync(modelRequest, cancellationToken);

        // Retry once if the model didn't produce any <file> blocks
        if (!response.ResponseText.Contains("<file ", StringComparison.Ordinal))
        {
            response = await _model.CompleteAsync(modelRequest with
            {
                UserPrompt = $"{userPrompt}\n\n{RetryPrompt}"
            }, cancellationToken);
        }

        return new AgentResult
        {
            AgentName      = Name,
            Status         = "Completed",
            Summary        = $"Code implementation generated for issue #{issueNumber}.",
            OutputMarkdown = response.ResponseText
        };
    }

    // Manifest order first (stable for review), then any extra files the model added.
    private static IEnumerable<FileChange> OrderEmitted(
        IReadOnlyList<ManifestItem> manifest, Dictionary<string, FileChange> emitted)
    {
        var inManifest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest)
        {
            if (emitted.TryGetValue(item.Path, out var change))
            {
                inManifest.Add(item.Path);
                yield return change;
            }
        }
        foreach (var (path, change) in emitted)
        {
            if (!inManifest.Contains(path))
                yield return change;
        }
    }

    private static Dictionary<string, string> BuildContextDocs(AgentContext ctx)
    {
        var docs = new Dictionary<string, string>();
        // Charter.Constraints.NeedsAuth selects the shell variant the app was seeded with: Yorrixx
        // applies the no-auth overlay (no Clerk / no Auth/ / no auth.spec) when NeedsAuth == false.
        // Absent (no charter, or a context built off the orchestrator's charter step) defaults to the
        // auth shell — today's behaviour; the real flow always sets needsAuth from the wizard-Required
        // charter field. See docs/roadmap/conditional-auth-yorrixx-brief.md.
        var needsAuth = !string.Equals(GetMeta(ctx, "needsAuth"), "false", StringComparison.OrdinalIgnoreCase);
        docs[ScaffoldContractLabel] = ScaffoldContractDocFor(needsAuth);
        AddIfPresent(docs, ctx, "repoContext",       "Repository Context");
        AddIfPresent(docs, ctx, "ownerBrief",       "Approved Product Brief");
        AddIfPresent(docs, ctx, "analystOutput",    "Business Analysis");
        AddIfPresent(docs, ctx, "architectOutput",  "Architecture Review");
        AddIfPresent(docs, ctx, "implSpec",         "Implementation Specification");
        AddIfPresent(docs, ctx, "poReviewFeedback", "Product Owner Review Feedback (fix these issues)");
        AddIfPresent(docs, ctx, "existingSource",   "Existing Source (current code — fix in place, do not regenerate)");
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
