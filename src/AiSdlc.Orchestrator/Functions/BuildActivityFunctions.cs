using AiSdlc.GitHub;
using AiSdlc.Orchestrator.Builds;
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
    private readonly ILogger<BuildActivityFunctions> _logger;

    public BuildActivityFunctions(IGitHubService gitHub, ILogger<BuildActivityFunctions> logger)
    {
        _gitHub = gitHub;
        _logger = logger;
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
