using System.Text;

namespace Yorrixx.DeployTemplate;

/// Renders `.github/workflows/deploy.yml` for every user-app repo — the single
/// source of truth for the deploy workflow. Pure + dependency-free so both the
/// SourceControl module (legacy in-process path) and the extracted provisioner
/// (responsibility-inversion new path — returns the rendered yaml in the
/// provision-result) render the IDENTICAL workflow, no drift.
///
/// The workflow authenticates to Azure via OIDC using the per-app deploy
/// identity — no long-lived secrets in the repo. The federated credential on
/// the App registration is server-side pinned to this repo + branch.
///
/// Full-stack: two parallel jobs (Vite frontend → F1 Web App; .NET Functions →
/// Flex Consumption). Static: a single job that zips the site files (no build,
/// no API). Names + identity + Clerk key are inlined at render time — nothing is
/// read from GitHub Actions vars/secrets at runtime.
public static class DeployWorkflowTemplate
{
    /// Placeholder the generator emits in mailto: links; Yorrixx swaps it for the
    /// contact address at deploy. MUST match PlatformContractMarkdown.ContactEmailToken
    /// (kept in sync by string equality — this assembly is dependency-free by design).
    public const string ContactEmailToken = "__CONTACT_EMAIL__";

    /// The fixed address every generated mailto: resolves to (MVP — one
    /// non-personal Yorrixx inbox for all apps; no per-app/owner email, no repo
    /// variable, no extra GitHub App permission). Committed app files keep the
    /// guard-safe token; the real value only appears in the deployed artifact.
    public const string ContactEmailValue = "placeholderemail@yorrixx.com";

    /// Placeholder the generator MUST use as the contact form's POST target.
    /// Yorrixx swaps it for the single shared relay URL (`{apiBaseUrl}/api/forms/submit`)
    /// at deploy — no appId/PII in the page (the app is identified server-side by
    /// the requesting host). MUST match PlatformContractMarkdown.FormEndpointToken.
    public const string FormEndpointToken = "__YORRIXX_FORM_ENDPOINT__";

    /// Placeholder for the shared form token (hidden field). Yorrixx swaps it for
    /// the `Forms:SharedToken` secret at deploy; the relay rejects mismatches.
    /// Guard-safe (opaque, no PII). MUST match PlatformContractMarkdown.FormTokenToken.
    public const string FormTokenToken = "__YORRIXX_FORM_TOKEN__";

    /// Emits a deploy step that replaces the contact-email placeholder with the
    /// fixed contact address across html/js/css under `root`, before the
    /// build/zip. `|` sed delimiter is safe (no valid email contains it);
    /// null-delimited grep|xargs handles odd filenames.
    private static void AppendContactEmailSubstitution(StringBuilder sb, string root)
    {
        sb.AppendLine("      - name: Substitute contact email");
        sb.AppendLine("        run: |");
        sb.AppendLine($"          grep -rlZ --include='*.html' --include='*.js' --include='*.css' " +
            $"'{ContactEmailToken}' {root} | xargs -0 -r sed -i \"s|{ContactEmailToken}|{ContactEmailValue}|g\"");
        sb.AppendLine();
    }

    /// Emits a deploy step that replaces the contact-form endpoint placeholder
    /// with this app's real Yorrixx relay URL across html/js under `root`, before
    /// the build/zip — so a generated marketing-page form POSTs to the FLAT relay
    /// `{apiBaseUrl}/api/forms/submit` (there are NO per-form routes — Q1a: this
    /// comment previously said /api/forms/{appId}/submit and misled codegen) while
    /// the committed source keeps the guard-safe token. No-op if the token is
    /// absent (apps with no form) or when appId/apiBaseUrl aren't known.
    private static void AppendFormSubstitution(StringBuilder sb, string root, string? apiBaseUrl, string? formSharedToken)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl) && string.IsNullOrWhiteSpace(formSharedToken)) return;
        sb.AppendLine("      - name: Substitute contact-form endpoint");
        sb.AppendLine("        run: |");
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            var endpoint = $"{apiBaseUrl.TrimEnd('/')}/api/forms/submit";
            sb.AppendLine($"          grep -rlZ --include='*.html' --include='*.js' " +
                $"'{FormEndpointToken}' {root} | xargs -0 -r sed -i \"s|{FormEndpointToken}|{endpoint}|g\"");
        }
        if (!string.IsNullOrWhiteSpace(formSharedToken))
        {
            sb.AppendLine($"          grep -rlZ --include='*.html' --include='*.js' " +
                $"'{FormTokenToken}' {root} | xargs -0 -r sed -i \"s|{FormTokenToken}|{formSharedToken}|g\"");
        }
        sb.AppendLine();
    }

    /// Emits a deploy step that guarantees the staged site has a favicon. The
    /// generator usually produces a brand `favicon.svg`, but the static template
    /// ships none so presence isn't guaranteed — some apps land with no tab icon.
    /// This runs on the staged `_site` (deployed artifact only — the repo is left
    /// untouched): if no favicon file exists it writes a neutral default, and if
    /// index.html has no icon <link> it injects one. Idempotent + brand-safe (an
    /// AI-made favicon is kept as-is). SVG favicons render in all current
    /// browsers; this is the deploy-time floor, not a replacement for a brand icon.
    private static void AppendFaviconFallback(StringBuilder sb)
    {
        sb.AppendLine("      - name: Ensure favicon");
        sb.AppendLine("        run: |");
        sb.AppendLine("          cd _site");
        sb.AppendLine("          if ! ls favicon.svg favicon.ico favicon.png >/dev/null 2>&1; then");
        sb.AppendLine("            cat > favicon.svg <<'SVG'");
        sb.AppendLine("          <svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"Site icon\">" +
            "<rect width=\"64\" height=\"64\" rx=\"14\" fill=\"#14130F\"/>" +
            "<circle cx=\"32\" cy=\"32\" r=\"12\" fill=\"#ffffff\"/></svg>");
        sb.AppendLine("          SVG");
        sb.AppendLine("          fi");
        // Inject a <link> right after <head> when the page declares no icon link.
        // 0,/<head>/ limits the substitution to the first match; & re-emits it.
        sb.AppendLine("          if [ -f index.html ] && ! grep -qiE 'rel=\"[^\"]*icon' index.html; then");
        sb.AppendLine("            ICON=$(ls favicon.svg >/dev/null 2>&1 && echo 'favicon.svg' || (ls favicon.png >/dev/null 2>&1 && echo 'favicon.png' || echo 'favicon.ico'))");
        sb.AppendLine("            TYPE=$(case \"$ICON\" in *.svg) echo 'image/svg+xml';; *.png) echo 'image/png';; *) echo 'image/x-icon';; esac)");
        sb.AppendLine("            sed -i \"0,/<head[^>]*>/s##&\\n  <link rel=\\\"icon\\\" type=\\\"$TYPE\\\" href=\\\"/$ICON\\\" />#\" index.html");
        sb.AppendLine("          fi");
        sb.AppendLine();
    }

    /// OIDC federated `az login` with retry. A deploy SP's role assignments are
    /// granted at provision time, but Azure RBAC can take a few minutes to
    /// propagate — so the very first deploy often raced ahead and `azure/login`
    /// failed with "No subscriptions found". This re-fetches the GitHub OIDC token
    /// and re-logs-in each attempt (re-login re-enumerates subscriptions), so it
    /// self-heals once the role lands. Self-contained (curl + jq + az, all on the
    /// ubuntu-latest runner) — no third-party action. Needs `id-token: write`.
    private static void AppendAzureLoginWithRetry(StringBuilder sb, string clientId, string tenantId, string subscriptionId)
    {
        sb.AppendLine("      - name: Azure login (OIDC, retry for RBAC propagation)");
        sb.AppendLine("        run: |");
        sb.AppendLine("          for i in $(seq 1 8); do");
        sb.AppendLine("            TOKEN=$(curl -sS -H \"Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN\" \\");
        sb.AppendLine("              \"$ACTIONS_ID_TOKEN_REQUEST_URL&audience=api://AzureADTokenExchange\" | jq -r '.value')");
        sb.AppendLine($"            if az login --service-principal -u {clientId} -t {tenantId} \\");
        sb.AppendLine("                 --federated-token \"$TOKEN\" --allow-no-subscriptions >/dev/null 2>&1 \\");
        sb.AppendLine($"               && [ -n \"$(az account list --query \"[?id=='{subscriptionId}'].id\" -o tsv)\" ]; then");
        sb.AppendLine($"              az account set --subscription {subscriptionId}");
        sb.AppendLine("              echo \"azure login ok (attempt $i)\"; break");
        sb.AppendLine("            fi");
        sb.AppendLine("            if [ \"$i\" = \"8\" ]; then echo \"::error::azure login failed after retries (RBAC propagation)\"; exit 1; fi");
        sb.AppendLine("            echo \"login/role not ready (attempt $i) — waiting 20s for RBAC propagation\"; sleep 20");
        sb.AppendLine("          done");
        sb.AppendLine();
    }

    public static string Render(
        string repoOwner,
        string repoName,
        string tenantId,
        string clientId,
        string subscriptionId,
        string resourceGroup,
        string frontendWebAppName,
        string apiFunctionAppName,
        string defaultBranch,
        string? clerkPublishableKey = null,
        bool isStatic = false,
        string? apiBaseUrl = null,
        string? formSharedToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontendWebAppName);
        // A Static-profile app has no API Function App (stack-profiles-static-first):
        // the apiFunctionAppName target is intentionally empty, so only require it
        // for the full-stack render that actually deploys an API.
        if (!isStatic) ArgumentException.ThrowIfNullOrWhiteSpace(apiFunctionAppName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBranch);

        if (isStatic)
        {
            return RenderStatic(
                repoOwner, repoName, tenantId, clientId, subscriptionId, resourceGroup,
                frontendWebAppName, defaultBranch, apiBaseUrl, formSharedToken);
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Generated by Yorrixx — do not edit by hand.");
        sb.AppendLine($"# Repo: {repoOwner}/{repoName}");
        sb.AppendLine("# Deploys both compute tiers on every push to the default branch:");
        sb.AppendLine("#   * Frontend (Vite, F1 Web App)        via azure/webapps-deploy@v3");
        sb.AppendLine("#   * API     (Functions, Flex Consumption) via azure/functions-action@v1");
        sb.AppendLine("# Both authenticate via OIDC against a per-app Entra app registration;");
        sb.AppendLine("# the federated credential restricts which repo+ref can mint tokens, so");
        sb.AppendLine("# this workflow can only ever deploy this app.");
        sb.AppendLine();
        sb.AppendLine("name: deploy");
        sb.AppendLine();
        sb.AppendLine("on:");
        sb.AppendLine("  push:");
        sb.AppendLine($"    branches: [{defaultBranch}]");
        sb.AppendLine("  workflow_dispatch:");
        sb.AppendLine();
        sb.AppendLine("permissions:");
        sb.AppendLine("  id-token: write   # required to mint the OIDC JWT for azure/login");
        sb.AppendLine("  contents: read");
        sb.AppendLine();
        sb.AppendLine("jobs:");
        sb.AppendLine();
        sb.AppendLine("  deploy-frontend:");
        sb.AppendLine("    runs-on: ubuntu-latest");
        sb.AppendLine("    steps:");
        sb.AppendLine("      - uses: actions/checkout@v4");
        sb.AppendLine();
        sb.AppendLine("      - uses: actions/setup-node@v4");
        sb.AppendLine("        with:");
        sb.AppendLine("          node-version: '22'");
        sb.AppendLine();

        // Contact-email substitution (see RenderStatic) — swap __CONTACT_EMAIL__
        // in the frontend source BEFORE the Vite build so the bundle ships the
        // real address. Value comes from the repo CONTACT_EMAIL Actions variable.
        AppendContactEmailSubstitution(sb, "src/frontend/src");
        // Marketing-page contact form → shared Yorrixx relay endpoint + token.
        // No-op when the app has no such form (tokens absent).
        AppendFormSubstitution(sb, "src/frontend/src", apiBaseUrl, formSharedToken);

        // Vite bakes env vars in at build time, not runtime. VITE_API_BASE_URL
        // points the SPA at its Function App — relative `/api/*` URLs hit
        // the frontend host instead and 404 (the v001 lesson). The name matches
        // the template's lib/api.ts (scaffold-first). The Clerk publishable key
        // is public by design — safe to inline.
        sb.AppendLine("      - name: Write Vite build-time env");
        sb.AppendLine("        run: |");
        sb.AppendLine($"          echo \"VITE_API_BASE_URL=https://{apiFunctionAppName}.azurewebsites.net\" > src/frontend/.env.production");
        if (!string.IsNullOrWhiteSpace(clerkPublishableKey))
        {
            sb.AppendLine($"          echo \"VITE_CLERK_PUBLISHABLE_KEY={clerkPublishableKey}\" >> src/frontend/.env.production");
        }
        sb.AppendLine();

        sb.AppendLine("      - run: npm install");
        sb.AppendLine("        working-directory: src/frontend");
        sb.AppendLine();
        sb.AppendLine("      - run: npm run build");
        sb.AppendLine("        working-directory: src/frontend");
        sb.AppendLine();
        sb.AppendLine("      - name: Zip frontend bundle");
        sb.AppendLine("        run: cd src/frontend/dist && zip -r \"$GITHUB_WORKSPACE/frontend.zip\" .");
        sb.AppendLine();
        sb.AppendLine("      - name: Azure login (OIDC)");
        sb.AppendLine("        uses: azure/login@v2");
        sb.AppendLine("        with:");
        sb.AppendLine($"          client-id: {clientId}");
        sb.AppendLine($"          tenant-id: {tenantId}");
        sb.AppendLine($"          subscription-id: {subscriptionId}");
        sb.AppendLine();
        sb.AppendLine("      - name: Deploy frontend Web App");
        sb.AppendLine("        uses: azure/webapps-deploy@v3");
        sb.AppendLine("        with:");
        sb.AppendLine($"          app-name: {frontendWebAppName}");
        sb.AppendLine("          package: frontend.zip");
        sb.AppendLine();
        sb.AppendLine("  deploy-api:");
        sb.AppendLine("    runs-on: ubuntu-latest");
        sb.AppendLine("    steps:");
        sb.AppendLine("      - uses: actions/checkout@v4");
        sb.AppendLine();
        sb.AppendLine("      - uses: actions/setup-dotnet@v4");
        sb.AppendLine("        with:");
        sb.AppendLine("          dotnet-version: '9.0.x'");
        sb.AppendLine();
        sb.AppendLine("      - name: Publish API");
        sb.AppendLine("        run: dotnet publish src/api --configuration Release --output publish");
        sb.AppendLine();
        sb.AppendLine("      - name: Zip API bundle");
        sb.AppendLine("        run: cd publish && zip -r \"$GITHUB_WORKSPACE/api.zip\" .");
        sb.AppendLine();
        sb.AppendLine("      - name: Azure login (OIDC)");
        sb.AppendLine("        uses: azure/login@v2");
        sb.AppendLine("        with:");
        sb.AppendLine($"          client-id: {clientId}");
        sb.AppendLine($"          tenant-id: {tenantId}");
        sb.AppendLine($"          subscription-id: {subscriptionId}");
        sb.AppendLine();
        sb.AppendLine("      - name: Deploy API Function App");
        sb.AppendLine("        uses: azure/functions-action@v1");
        sb.AppendLine("        with:");
        sb.AppendLine($"          app-name: {apiFunctionAppName}");
        sb.AppendLine("          package: api.zip");
        return sb.ToString();
    }

    /// Static-profile render (stack-profiles-static-first): plain HTML/CSS/JS, no
    /// build, no API. Publishes to an **Azure Storage static website** (`$web`
    /// container — no App Service plan, no SWA, so it sidesteps the F1 cap AND the
    /// Free-SWA 10-per-subscription cap). Auth is OIDC against the per-app Entra
    /// federated identity; the site is uploaded with `az storage blob upload-batch
    /// --auth-mode login` (the deploy SP has Storage Blob Data Contributor on the
    /// account), so **no secret/token is stored in the repo**. `storageAccountName`
    /// is the storage account (Hosting carries it in DeployedApp.FrontendWebAppName).
    private static string RenderStatic(
        string repoOwner,
        string repoName,
        string tenantId,
        string clientId,
        string subscriptionId,
        string resourceGroup,
        string storageAccountName,
        string defaultBranch,
        string? apiBaseUrl,
        string? formSharedToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Generated by Yorrixx — do not edit by hand.");
        sb.AppendLine($"# Repo: {repoOwner}/{repoName}");
        sb.AppendLine("# Static-profile app: publishes the HTML/CSS/JS site to an Azure Storage");
        sb.AppendLine("# static website ($web) on every push to the default branch. No build, no API.");
        sb.AppendLine("# Auth is OIDC against a per-app Entra federated identity; content is uploaded");
        sb.AppendLine("# with `az storage blob upload-batch --auth-mode login` (no secret in the repo).");
        sb.AppendLine();
        sb.AppendLine("name: deploy");
        sb.AppendLine();
        sb.AppendLine("on:");
        sb.AppendLine("  push:");
        sb.AppendLine($"    branches: [{defaultBranch}]");
        sb.AppendLine("  workflow_dispatch:");
        sb.AppendLine();
        sb.AppendLine("permissions:");
        sb.AppendLine("  id-token: write   # required to mint the OIDC JWT for azure/login");
        sb.AppendLine("  contents: read");
        sb.AppendLine();
        sb.AppendLine("jobs:");
        sb.AppendLine();
        sb.AppendLine("  deploy-frontend:");
        sb.AppendLine("    runs-on: ubuntu-latest");
        sb.AppendLine("    steps:");
        sb.AppendLine("      - uses: actions/checkout@v4");
        sb.AppendLine();
        // Contact-email substitution before staging — swap __CONTACT_EMAIL__ for
        // the fixed address so the shipped site has a real mailto. No-op if absent.
        AppendContactEmailSubstitution(sb, ".");
        // Marketing-page contact form → shared Yorrixx relay endpoint + token.
        // No-op when the app has no such form (tokens absent).
        AppendFormSubstitution(sb, ".", apiBaseUrl, formSharedToken);
        // Stage only the site files — exclude engineering scaffolding (CI, e2e
        // harness, Yorrixx spec/profile, docs) that must not be served.
        sb.AppendLine("      - name: Stage site files");
        sb.AppendLine("        run: |");
        sb.AppendLine("          mkdir -p _site");
        sb.AppendLine("          rsync -a --exclude='.git' --exclude='.github' --exclude='tests' " +
            "--exclude='.yorrixx' --exclude='_site' --exclude='*.md' --exclude='.gitignore' " +
            "--exclude='.htmlvalidate.json' --exclude='.ai-sdlc.yml' ./ _site/");
        sb.AppendLine();
        // Guarantee a tab icon on the deployed artifact (brand favicon kept if present).
        AppendFaviconFallback(sb);
        AppendAzureLoginWithRetry(sb, clientId, tenantId, subscriptionId);
        // Publish the staged site to the storage account's $web container via the
        // OIDC-authorized deploy SP (Storage Blob Data Contributor). '$web' is
        // single-quoted so the shell doesn't expand it. --overwrite for redeploys.
        sb.AppendLine("      - name: Deploy to storage static website");
        sb.AppendLine("        run: |");
        sb.AppendLine($"          az storage blob upload-batch --account-name {storageAccountName} " +
            "--auth-mode login --source _site --destination '$web' --overwrite");
        return sb.ToString();
    }
}
