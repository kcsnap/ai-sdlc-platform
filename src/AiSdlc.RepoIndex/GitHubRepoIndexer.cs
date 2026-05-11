using AiSdlc.GitHub;

namespace AiSdlc.RepoIndex;

public sealed class GitHubRepoIndexer : IRepoIndexer
{
    private readonly IGitHubService _gitHub;

    public GitHubRepoIndexer(IGitHubService gitHub)
    {
        _gitHub = gitHub;
    }

    public async Task<RepoIndex?> IndexAsync(string repository, CancellationToken cancellationToken)
    {
        var yaml = await _gitHub.GetFileContentAsync(repository, ".ai-sdlc.yml", cancellationToken);
        if (string.IsNullOrWhiteSpace(yaml)) return null;

        var config = AiSdlcConfig.Parse(yaml);
        if (config is null) return null;

        return new RepoIndex
        {
            Repository    = repository,
            Description   = config.Repo?.Description ?? string.Empty,
            Stack         = MapStack(config.Stack),
            Pages         = config.Stack?.Frontend?.Pages
                                .Select(p => new PageInfo { Path = p.Path ?? "", Component = p.Component ?? "", Description = p.Description ?? "" })
                                .ToArray() ?? [],
            ApiEndpoints  = config.Stack?.Api?.Endpoints.AsReadOnly() ?? (IReadOnlyList<string>)[],
            DatabaseTables = config.Stack?.Database?.Tables.AsReadOnly() ?? (IReadOnlyList<string>)[],
            HighRiskPaths  = config.RiskAreas?.High.AsReadOnly()   ?? (IReadOnlyList<string>)[],
            MediumRiskPaths = config.RiskAreas?.Medium.AsReadOnly() ?? (IReadOnlyList<string>)[],
            LowRiskPaths   = config.RiskAreas?.Low.AsReadOnly()    ?? (IReadOnlyList<string>)[],
            BranchNaming   = config.BranchNaming ?? string.Empty,
            IndexedAtUtc   = DateTimeOffset.UtcNow
        };
    }

    private static StackInfo MapStack(AiSdlcConfig.StackSection? s) => new()
    {
        Frontend = s?.Frontend is { } f ? new FrontendInfo { Framework = f.Framework ?? "", Language = f.Language ?? "", Location = f.Location ?? "" } : null,
        Api      = s?.Api      is { } a ? new ApiInfo      { Framework = a.Framework ?? "", Language = a.Language ?? "", Location = a.Location ?? "" } : null,
        Database = s?.Database is { } d ? new DatabaseInfo { Engine = d.Engine ?? "", Orm = d.Orm ?? "", Migrations = d.Migrations ?? "" } : null
    };
}
