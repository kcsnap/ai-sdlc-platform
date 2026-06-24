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
    private readonly ILogger<BuildActivityFunctions> _logger;

    public BuildActivityFunctions(IGitHubService gitHub, IProvisionerClient provisioner, ILogger<BuildActivityFunctions> logger)
    {
        _gitHub      = gitHub;
        _provisioner = provisioner;
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

    [Function(nameof(ApplyDeployConfigAsync))]
    public async Task ApplyDeployConfigAsync([ActivityTrigger] ApplyDeployConfigInput input, CancellationToken cancellationToken)
    {
        // Write the deploy identity (OIDC client/tenant/subscription) + Clerk publishable key as repo
        // Actions variables so the template's deploy.yml can azure/login@v2 and configure the app.
        foreach (var (name, value) in DeployVariables(input.Deploy, input.ClerkPublishableKey))
        {
            _logger.LogInformation("Setting repo variable {Name} on {Repo}.", name, input.Repository);
            await _gitHub.SetRepoVariableAsync(input.Repository, name, value, cancellationToken);
        }
    }

    // The deploy vars to set, skipping any the result didn't supply. AZURE_* feed azure/login@v2 (OIDC);
    // resource names are derived from appId by the provisioner, so they need no vars here.
    internal static IReadOnlyList<(string Name, string Value)> DeployVariables(ProvisionDeploy? deploy, string? clerkPublishableKey)
    {
        var vars = new List<(string, string)>();
        void Add(string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) vars.Add((name, value!)); }

        if (deploy is not null)
        {
            Add("AZURE_CLIENT_ID", deploy.ClientId);
            Add("AZURE_TENANT_ID", deploy.TenantId);
            Add("AZURE_SUBSCRIPTION_ID", deploy.SubscriptionId);
        }
        Add("CLERK_PUBLISHABLE_KEY", clerkPublishableKey);
        return vars;
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
