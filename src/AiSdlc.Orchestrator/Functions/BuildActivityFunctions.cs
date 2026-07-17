using System.Net.Http;
using System.Text;
using AiSdlc.GitHub;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.Orchestrator.Provisioning;
using Yorrixx.Provisioner.Contracts;
using AiSdlc.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AiSdlc.Orchestrator.Functions;

/// <summary>
/// Activities for the new-path build (API-initiated). Component 3: create the user-app repo from the
/// stack-appropriate template via the GitHub App (Administration:write). The template is selected by the
/// deterministic stack profile; the repo is created PRIVATE under the apps org.
/// </summary>
public sealed class BuildActivityFunctions
{
    internal const string DefaultTemplateOwner     = "yorrixx-apps";
    internal const string DefaultStaticTemplate    = "ai-sdlc-static-template";
    internal const string DefaultFullStackTemplate = "ai-sdlc-react-dotnet-template";

    private readonly IGitHubService _gitHub;
    private readonly IProvisionerClient _provisioner;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BuildActivityFunctions> _logger;

    public BuildActivityFunctions(
        IGitHubService gitHub, IProvisionerClient provisioner, IHttpClientFactory httpFactory,
        ILogger<BuildActivityFunctions> logger)
    {
        _gitHub      = gitHub;
        _provisioner = provisioner;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    [Function(nameof(CreateUserAppRepoAsync))]
    public async Task<CreatedRepository> CreateUserAppRepoAsync(
        [ActivityTrigger] CreateRepoInput input, CancellationToken cancellationToken)
    {
        var owner    = Environment.GetEnvironmentVariable("TemplateOwner") ?? DefaultTemplateOwner;
        var template = ResolveTemplateRepo(
            input.StackProfile, owner,
            Environment.GetEnvironmentVariable("StaticTemplateRepo")    ?? DefaultStaticTemplate,
            Environment.GetEnvironmentVariable("FullStackTemplateRepo") ?? DefaultFullStackTemplate);
        var name = RepoName(input.AppId);

        _logger.LogInformation(
            "Creating repo {Owner}/{Name} from template {Template} (profile {Profile}).",
            owner, name, template, input.StackProfile);

        return await _gitHub.CreateRepositoryFromTemplateAsync(
            template, owner, name, isPrivate: true,
            description: $"Yorrixx-generated app {input.AppId} ({input.StackProfile})",
            cancellationToken);
    }

    [Function(nameof(StartProvisionAsync))]
    public async Task StartProvisionAsync([ActivityTrigger] ProvisionSpec request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Provisioning {AppId} (build {BuildId}, {Profile}).", request.AppId, request.BuildId, request.StackProfile);
        await _provisioner.StartProvisionAsync(request, cancellationToken);
    }

    [Function(nameof(PollProvisionResultAsync))]
    public Task<ProvisionResult?> PollProvisionResultAsync([ActivityTrigger] string buildId, CancellationToken cancellationToken)
        => _provisioner.GetProvisionResultAsync(buildId, cancellationToken);

    [Function(nameof(CommitDeployWorkflowAsync))]
    public async Task CommitDeployWorkflowAsync([ActivityTrigger] CommitDeployInput input, CancellationToken cancellationToken)
    {
        // Commit the provisioner's canonical, fully-resolved deploy workflow VERBATIM — the platform never
        // renders its own deploy.yml. (Requires App Workflows:write to push under .github/workflows/.)
        _logger.LogInformation("Committing deploy.yml to {Repo}@{Branch}.", input.Repository, input.Branch);
        await _gitHub.CreateOrUpdateFileAsync(
            input.Repository, ".github/workflows/deploy.yml", input.DeployYaml,
            "ci: add resolved deploy workflow", input.Branch, cancellationToken);
    }

    [Function(nameof(GetDeployStatusAsync))]
    public async Task<string> GetDeployStatusAsync([ActivityTrigger] DeployStatusInput input, CancellationToken cancellationToken)
    {
        var checks = await _gitHub.GetCheckRunResultsAsync(input.Repository, input.Reference, cancellationToken);
        return SummarizeDeploy(checks);
    }

    // F3: the platform seeds the ai-sdlc:bootstrap issue on API-initiated repos (it created the repo, so no
    // external party exists to open it — the silent hole that shipped scaffold content on flip #4).
    [Function(nameof(SeedBootstrapIssueAsync))]
    public async Task<GitHubIssueReference> SeedBootstrapIssueAsync(
        [ActivityTrigger] SeedBootstrapIssueInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding bootstrap issue on {Repository}.", input.Repository);
        return await _gitHub.CreateIssueAsync(
            input.Repository, input.Title, input.Body,
            [Webhooks.GitHubWebhookProcessor.BootstrapLabel], cancellationToken);
    }

    // F1: the review gate's dev-convenience switch. Orchestrator code must stay deterministic, so the env
    // read happens in an activity. Default (unset/anything-but-true) = gate ON.
    [Function(nameof(GetReviewAutoApproveAsync))]
    public Task<bool> GetReviewAutoApproveAsync([ActivityTrigger] string? _)
        => Task.FromResult(string.Equals(
            Environment.GetEnvironmentVariable("AutoApproveReview"), "true", StringComparison.OrdinalIgnoreCase));

    // F3(b)/Q1(c): fetch the hosted page's HTML so verification can assert real charter-derived content
    // (and retry past a stale-CDN window). Empty string on any failure — the checks treat that as fail.
    [Function(nameof(FetchPageAsync))]
    public async Task<string> FetchPageAsync([ActivityTrigger] string url, CancellationToken cancellationToken)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var html = await http.GetStringAsync(url, cancellationToken);
            return html.Length <= 65536 ? html : html[..65536];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Page fetch of {Url} failed.", url);
            return string.Empty;
        }
    }

    [Function(nameof(ProbeUrlAsync))]
    public async Task<int> ProbeUrlAsync([ActivityTrigger] string url, CancellationToken cancellationToken)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            using var response = await http.GetAsync(url, cancellationToken);
            return (int)response.StatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Probe of {Url} failed.", url);
            return 0;
        }
    }

    [Function(nameof(SendCallbackAsync))]
    public async Task SendCallbackAsync([ActivityTrigger] CallbackMessage message, CancellationToken cancellationToken)
    {
        // G6 P4: a dead callback must be VISIBLE, never silently green. Retry transient failures here,
        // then THROW — the activity shows Failed in the orchestration history and the orchestrator
        // records it in the run status (without failing the build).
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };
        int? lastStatus = null;
        Exception? lastError = null;

        foreach (var delay in delays)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            try
            {
                using var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(20);
                using var request = new HttpRequestMessage(HttpMethod.Post, CallbackUrl(message.CallbackBaseUrl, message.AppId, message.Kind));
                var adminKey = Environment.GetEnvironmentVariable("YorrixxAdminKey");
                if (!string.IsNullOrWhiteSpace(adminKey))
                    request.Headers.Add("X-Yorrixx-Admin-Key", adminKey);
                request.Content = new StringContent(message.PayloadJson, Encoding.UTF8, "application/json");

                using var response = await http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;

                lastStatus = (int)response.StatusCode;
                _logger.LogWarning("Callback {Kind} for {AppId} returned {Status}.", message.Kind, message.AppId, lastStatus);

                // Deterministic 4xx (bad key, unknown app) won't heal on retry — fail fast.
                if (lastStatus is >= 400 and < 500 and not (408 or 429))
                    break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Callback {Kind} for {AppId} attempt failed.", message.Kind, message.AppId);
            }
        }

        _logger.LogError(lastError,
            "Callback {Kind} for {AppId} FAILED after retries (lastStatus={Status}).",
            message.Kind, message.AppId, lastStatus);
        throw new InvalidOperationException(
            $"Callback {message.Kind} for {message.AppId} failed (lastStatus={lastStatus?.ToString() ?? lastError?.GetType().Name ?? "none"}).",
            lastError);
    }

    internal static string CallbackUrl(string callbackBaseUrl, string appId, string kind) =>
        $"{callbackBaseUrl.TrimEnd('/')}/apps/{appId}/{kind}";

    // none = no checks yet · running = any not completed · success = all completed+success · else failed.
    internal static string SummarizeDeploy(IReadOnlyList<CheckRunResult> checks)
    {
        if (checks.Count == 0) return "none";
        if (checks.Any(c => !string.Equals(c.Status, "completed", StringComparison.OrdinalIgnoreCase))) return "running";
        return checks.All(c => string.Equals(c.Conclusion, "success", StringComparison.OrdinalIgnoreCase)) ? "success" : "failed";
    }

    // Deploy-substituted tokens (__CONTACT_EMAIL__ style) surviving to the LIVE page mean the deploy-time
    // substitution never ran — positive scaffold evidence. Uppercase-only so JS dunders (__proto__) can't
    // false-match.
    private static readonly System.Text.RegularExpressions.Regex UnsubstitutedDeployToken =
        new(@"__[A-Z][A-Z0-9_]*__", System.Text.RegularExpressions.RegexOptions.Compiled);

    // F3(b): scaffold detector — the flip-#4 canary went LIVE with raw template content and 3 green checks.
    // F4: fails ONLY on POSITIVE scaffold evidence (default title, unfilled {{ }} slots, unsubstituted
    // __TOKEN__ markers, or an empty page). A missing app name is NOT evidence — ramp-w1-florist shipped
    // a genuinely charter-derived site whose copy (sensibly) never rendered the harness slug, and the old
    // appName clause false-failed it. The name echo is the separate WARN-only AppNameEchoed check.
    internal static bool ContentLooksScaffold(string? pageHtml)
    {
        if (string.IsNullOrWhiteSpace(pageHtml)) return true; // unfetchable ⇒ cannot prove it's real content
        if (pageHtml.Contains("<title>App</title>", StringComparison.OrdinalIgnoreCase)) return true;
        if (pageHtml.Contains("{{", StringComparison.Ordinal) && pageHtml.Contains("}}", StringComparison.Ordinal))
            return true; // unfilled template tokens
        return UnsubstitutedDeployToken.IsMatch(pageHtml);
    }

    // D8: entity-escaped markup in the page SOURCE renders as literal "<svg …" text — fresh-w2-florist
    // shipped three feature icons as visible tag soup and every substring gate stayed green. Escaped
    // tag openers (&lt; followed by a letter or /) outside <pre>/<code> are deterministic evidence of
    // markup that was meant to render. Double-escaped forms (&amp;lt;) collapse to the same probe
    // after one decode, so a single check covers both.
    private static readonly System.Text.RegularExpressions.Regex EscapedTagOpener =
        new(@"&(?:amp;)?lt;/?[A-Za-z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex CodeLikeBlock =
        new(@"<(pre|code|script|style|textarea)\b.*?</\1\s*>", System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

    internal static bool HasEscapedMarkupInVisibleText(string? pageHtml)
    {
        if (string.IsNullOrWhiteSpace(pageHtml)) return false;
        var withoutCodeBlocks = CodeLikeBlock.Replace(pageHtml, string.Empty);
        return EscapedTagOpener.IsMatch(withoutCodeBlocks);
    }

    // Advisory only (F4): builders legitimately stylize or omit the charter app name in page copy.
    internal static bool AppNameEchoed(string? pageHtml, string? appName) =>
        !string.IsNullOrWhiteSpace(pageHtml)
        && !string.IsNullOrWhiteSpace(appName)
        && pageHtml.Contains(appName, StringComparison.OrdinalIgnoreCase);

    // Builds the verification check table from the deploy status + a hosted-URL probe (+ the fetched HTML
    // for the scaffold gate; pass null appName to skip the content check — test back-compat only).
    internal static VerificationResult AssembleVerification(
        string deployStatus, int servesStatus, bool isStatic, string? pageHtml = null, string? appName = null)
    {
        var serves = servesStatus is >= 200 and < 400;
        var checks = new List<VerificationCheck>
        {
            new("deploy-run-green", "Deploy workflow succeeded",
                string.Equals(deployStatus, "success", StringComparison.OrdinalIgnoreCase) ? "pass" : "fail",
                $"deploy={deployStatus}"),
            new("frontend-serves-app", "Frontend serves content", serves ? "pass" : "fail", $"HTTP {servesStatus}"),
            isStatic
                ? new("api-health", "API health", "skipped", "static app — no API")
                : new("api-health", "API health", serves ? "pass" : "fail", $"hosted URL HTTP {servesStatus}"),
        };
        if (appName is not null)
        {
            var scaffold = ContentLooksScaffold(pageHtml);
            checks.Add(new("content-not-scaffold", "Hosted content is charter-derived, not template scaffold",
                scaffold ? "fail" : "pass",
                scaffold
                    ? "page is empty or carries scaffold markers (default title / {{ }} / __TOKEN__)"
                    : "no scaffold markers on the page"));

            // WARN-only (F4): never fails the outcome — the name may be legitimately stylized or omitted.
            var echoed = AppNameEchoed(pageHtml, appName);
            checks.Add(new("app-name-echo", "Page mentions the charter app name",
                scaffold ? "skipped" : echoed ? "pass" : "warn",
                echoed ? $"page mentions '{appName}'" : $"'{appName}' not found verbatim — advisory only"));

            // D8: escaped markup rendering as visible text (fresh-w2-florist's tag-soup icons).
            var escapedMarkup = HasEscapedMarkupInVisibleText(pageHtml);
            checks.Add(new("no-escaped-markup", "No entity-escaped markup rendering as visible text",
                escapedMarkup ? "fail" : "pass",
                escapedMarkup ? "page contains escaped tag sequences (&lt;svg / &lt;div / &lt;/) outside code blocks"
                              : "no escaped tag sequences in visible text"));
        }
        var outcome = checks.Any(c => c.Status == "fail") ? "failed" : "passed";
        return new VerificationResult(outcome, checks);
    }

    // Static → the plain HTML/CSS template; anything else (FullStack) → the React+.NET template.
    internal static string ResolveTemplateRepo(string stackProfile, string owner, string staticTemplate, string fullStackTemplate)
    {
        var repo = string.Equals(stackProfile, nameof(Yorrixx.Contracts.Generation.StackProfile.Static), StringComparison.OrdinalIgnoreCase)
            ? staticTemplate
            : fullStackTemplate;
        return $"{owner}/{repo}";
    }

    // Contract (G6 P2): repos are user-app-{appId8}, where appId8 = first 8 chars of the hyphen-stripped,
    // lowercased appId (padded with '0' when shorter) — EXACTLY the provisioner's ResourceNames derivation,
    // so the deploy identity's federated-credential subject (repo:{owner}/user-app-{appId8}:ref:…) matches
    // the repo the workflow actually runs in. The full 32-char name broke OIDC on every G6 deploy.
    internal static string RepoName(string appId)
    {
        var clean = appId.Replace("-", "").ToLowerInvariant();
        var id8 = clean.Length >= 8 ? clean[..8] : clean.PadRight(8, '0');
        return $"user-app-{id8}";
    }
}
