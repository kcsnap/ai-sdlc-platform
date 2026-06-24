using System.Net.Http;
using System.Text;
using AiSdlc.GitHub;
using AiSdlc.Orchestrator.Builds;
using AiSdlc.Orchestrator.Provisioning;
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
    public async Task StartProvisionAsync([ActivityTrigger] ProvisionRequest request, CancellationToken cancellationToken)
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
        // Fire-and-forget: a callback failure (incl. a 404 for an unknown app) must never fail the build.
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
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Callback {Kind} for {AppId} returned {Status}.", message.Kind, message.AppId, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Callback {Kind} for {AppId} failed (fire-and-forget).", message.Kind, message.AppId);
        }
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

    // Builds the verification check table from the deploy status + a hosted-URL probe.
    internal static VerificationResult AssembleVerification(string deployStatus, int servesStatus, bool isStatic)
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
        var outcome = checks.Any(c => c.Status == "fail") ? "failed" : "passed";
        return new VerificationResult(outcome, checks);
    }

    // Static → the plain HTML/CSS template; anything else (FullStack) → the React+.NET template.
    internal static string ResolveTemplateRepo(string stackProfile, string owner, string staticTemplate, string fullStackTemplate)
    {
        var repo = string.Equals(stackProfile, nameof(AiSdlc.RepoIndex.Charter.StackProfile.Static), StringComparison.OrdinalIgnoreCase)
            ? staticTemplate
            : fullStackTemplate;
        return $"{owner}/{repo}";
    }

    internal static string RepoName(string appId) => $"user-app-{appId}";
}
