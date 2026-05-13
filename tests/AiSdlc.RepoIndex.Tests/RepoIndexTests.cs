using AiSdlc.GitHub;
using AiSdlc.RepoIndex;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.RepoIndex.Tests;

public sealed class AiSdlcConfigParseTests
{
    private const string FullYaml = """
        repo:
          name: launchcart
          description: E-commerce demo app
          owner: kcsnap

        stack:
          frontend:
            framework: React
            language: TypeScript
            location: src/frontend
            pages:
              - path: /
                component: HomePage
                description: Landing page
              - path: /products
                component: ProductListPage
                description: Browse products

          api:
            framework: ASP.NET Core
            language: C#
            location: src/api
            endpoints:
              - GET /api/products
              - POST /api/enquiries

          database:
            engine: PostgreSQL
            orm: EF Core
            migrations: src/api/Migrations
            tables:
              - Products
              - Enquiries

        risk_areas:
          high:
            - payment processing
          medium:
            - checkout flow
          low:
            - static pages

        branch_naming: "feature/{issue}-{slug}"
        """;

    [Fact]
    public void Parse_FullYaml_MapsRepoSection()
    {
        var config = AiSdlcConfig.Parse(FullYaml);

        Assert.NotNull(config?.Repo);
        Assert.Equal("launchcart", config.Repo.Name);
        Assert.Equal("E-commerce demo app", config.Repo.Description);
        Assert.Equal("kcsnap", config.Repo.Owner);
    }

    [Fact]
    public void Parse_FullYaml_MapsFrontend()
    {
        var config = AiSdlcConfig.Parse(FullYaml);

        var fe = config?.Stack?.Frontend;
        Assert.NotNull(fe);
        Assert.Equal("React", fe.Framework);
        Assert.Equal("TypeScript", fe.Language);
        Assert.Equal("src/frontend", fe.Location);
        Assert.Equal(2, fe.Pages.Count);
        Assert.Equal("/", fe.Pages[0].Path);
        Assert.Equal("HomePage", fe.Pages[0].Component);
    }

    [Fact]
    public void Parse_FullYaml_MapsApi()
    {
        var config = AiSdlcConfig.Parse(FullYaml);

        var api = config?.Stack?.Api;
        Assert.NotNull(api);
        Assert.Equal("ASP.NET Core", api.Framework);
        Assert.Contains("GET /api/products", api.Endpoints);
        Assert.Contains("POST /api/enquiries", api.Endpoints);
    }

    [Fact]
    public void Parse_FullYaml_MapsDatabase()
    {
        var config = AiSdlcConfig.Parse(FullYaml);

        var db = config?.Stack?.Database;
        Assert.NotNull(db);
        Assert.Equal("PostgreSQL", db.Engine);
        Assert.Equal("EF Core", db.Orm);
        Assert.Contains("Products", db.Tables);
        Assert.Contains("Enquiries", db.Tables);
    }

    [Fact]
    public void Parse_FullYaml_MapsRiskAreas()
    {
        var config = AiSdlcConfig.Parse(FullYaml);

        Assert.NotNull(config?.RiskAreas);
        Assert.Contains("payment processing", config.RiskAreas.High);
        Assert.Contains("checkout flow", config.RiskAreas.Medium);
        Assert.Contains("static pages", config.RiskAreas.Low);
    }

    [Fact]
    public void Parse_FullYaml_MapsBranchNaming()
    {
        var config = AiSdlcConfig.Parse(FullYaml);

        Assert.Equal("feature/{issue}-{slug}", config?.BranchNaming);
    }

    [Fact]
    public void Parse_EmptyYaml_ReturnsNullOrEmpty()
    {
        var config = AiSdlcConfig.Parse("{}");

        Assert.NotNull(config);
        Assert.Null(config.Repo);
        Assert.Null(config.Stack);
    }

    [Fact]
    public void Parse_AutomationSection_MapsFlags()
    {
        const string yaml = """
            automation:
              allow_low_risk_auto_merge: true
              allow_low_risk_production_deploy: false
            """;

        var config = AiSdlcConfig.Parse(yaml);

        Assert.True(config?.Automation?.AllowLowRiskAutoMerge);
        Assert.False(config?.Automation?.AllowLowRiskProductionDeploy);
    }

    [Fact]
    public void Parse_UnknownKeys_AreIgnored()
    {
        const string yaml = """
            unknown_key: value
            repo:
              name: test
            """;

        var config = AiSdlcConfig.Parse(yaml);

        Assert.NotNull(config);
        Assert.Equal("test", config.Repo?.Name);
    }
}

public sealed class RepoIndexMarkdownRendererTests
{
    [Fact]
    public void Render_FullIndex_ContainsAllSections()
    {
        var index = new global::AiSdlc.RepoIndex.RepoIndex
        {
            Repository    = "kcsnap/launchcart",
            Description   = "E-commerce demo app",
            Stack         = new StackInfo
            {
                Frontend = new FrontendInfo { Framework = "React", Language = "TypeScript", Location = "src/frontend" },
                Api      = new ApiInfo      { Framework = "ASP.NET Core", Language = "C#", Location = "src/api" },
                Database = new DatabaseInfo { Engine = "PostgreSQL", Orm = "EF Core", Migrations = "src/api/Migrations" }
            },
            Pages          = [new PageInfo { Path = "/", Component = "HomePage", Description = "Landing" }],
            ApiEndpoints   = ["GET /api/products"],
            DatabaseTables = ["Products", "Enquiries"],
            HighRiskPaths  = ["payment processing"],
            MediumRiskPaths = ["checkout flow"],
        };

        var md = RepoIndexMarkdownRenderer.Render(index);

        Assert.Contains("kcsnap/launchcart", md);
        Assert.Contains("E-commerce demo app", md);
        Assert.Contains("React", md);
        Assert.Contains("ASP.NET Core", md);
        Assert.Contains("PostgreSQL", md);
        Assert.Contains("HomePage", md);
        Assert.Contains("GET /api/products", md);
        Assert.Contains("Products, Enquiries", md);
        Assert.Contains("payment processing", md);
        Assert.Contains("checkout flow", md);
    }

    [Fact]
    public void Render_NoPages_OmitsPagesSection()
    {
        var index = new global::AiSdlc.RepoIndex.RepoIndex
        {
            Repository  = "kcsnap/test",
            Description = string.Empty,
            Stack       = new StackInfo()
        };

        var md = RepoIndexMarkdownRenderer.Render(index);

        Assert.DoesNotContain("### Pages", md);
        Assert.DoesNotContain("### API Endpoints", md);
        Assert.DoesNotContain("### Database Tables", md);
        Assert.DoesNotContain("### Risk Areas", md);
    }
}

public sealed class GitHubRepoIndexerTests
{
    [Fact]
    public async Task IndexAsync_FileNotFound_ReturnsNull()
    {
        var indexer = new GitHubRepoIndexer(new StubGitHubService(null));

        var result = await indexer.IndexAsync("owner/repo", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IndexAsync_ValidYaml_ReturnsMappedIndex()
    {
        const string yaml = """
            repo:
              name: launchcart
              description: Demo app
            stack:
              frontend:
                framework: React
                language: TypeScript
                location: src/frontend
                pages:
                  - path: /
                    component: HomePage
                    description: Home
              api:
                framework: ASP.NET Core
                language: C#
                location: src/api
                endpoints:
                  - GET /api/products
              database:
                engine: PostgreSQL
                orm: EF Core
                migrations: src/api/Migrations
                tables:
                  - Products
            """;

        var indexer = new GitHubRepoIndexer(new StubGitHubService(yaml));

        var result = await indexer.IndexAsync("kcsnap/launchcart", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("kcsnap/launchcart", result.Repository);
        Assert.Equal("Demo app", result.Description);
        Assert.Equal("React", result.Stack.Frontend?.Framework);
        Assert.Equal("ASP.NET Core", result.Stack.Api?.Framework);
        Assert.Equal("PostgreSQL", result.Stack.Database?.Engine);
        Assert.Single(result.Pages);
        Assert.Equal("/", result.Pages[0].Path);
        Assert.Single(result.ApiEndpoints);
        Assert.Equal("GET /api/products", result.ApiEndpoints[0]);
        Assert.Single(result.DatabaseTables);
        Assert.Equal("Products", result.DatabaseTables[0]);
    }

    [Fact]
    public async Task IndexAsync_AutomationFlags_FlowThroughToRepoIndex()
    {
        const string yaml = """
            repo:
              name: launchcart
            automation:
              allow_low_risk_auto_merge: true
              allow_low_risk_production_deploy: true
            """;

        var indexer = new GitHubRepoIndexer(new StubGitHubService(yaml));

        var result = await indexer.IndexAsync("owner/repo", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.AllowLowRiskAutoMerge);
        Assert.True(result.AllowLowRiskProductionDeploy);
    }

    [Fact]
    public async Task IndexAsync_NoAutomationSection_DefaultsToFalse()
    {
        const string yaml = "repo:\n  name: launchcart";

        var indexer = new GitHubRepoIndexer(new StubGitHubService(yaml));

        var result = await indexer.IndexAsync("owner/repo", CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.AllowLowRiskAutoMerge);
        Assert.False(result.AllowLowRiskProductionDeploy);
    }

    private sealed class StubGitHubService(string? content) : IGitHubService
    {
        public Task<string?> GetFileContentAsync(string repository, string path, CancellationToken cancellationToken)
            => Task.FromResult(content);

        public Task<IssueDetails> GetIssueAsync(string r, int n, CancellationToken c) => throw new NotImplementedException();
        public Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(string r, int n, CancellationToken c) => throw new NotImplementedException();
        public Task<IssueComment> AddIssueCommentAsync(string r, int n, string m, CancellationToken c) => throw new NotImplementedException();
        public Task<IssueComment> AddPullRequestCommentAsync(string r, int n, string m, CancellationToken c) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> AddLabelsAsync(string r, int n, IReadOnlyList<string> l, CancellationToken c) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> RemoveLabelsAsync(string r, int n, IReadOnlyList<string> l, CancellationToken c) => throw new NotImplementedException();
        public Task<GitHubPullRequestReference> CreatePullRequestAsync(CreatePullRequestRequest req, CancellationToken c) => throw new NotImplementedException();
        public Task<PullRequestDetails> GetPullRequestAsync(string r, int n, CancellationToken c) => throw new NotImplementedException();
        public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string r, int n, CancellationToken c) => throw new NotImplementedException();
        public Task<IReadOnlyList<CheckRunResult>> GetCheckRunResultsAsync(string r, string re, CancellationToken c) => throw new NotImplementedException();
        public Task MergePullRequestAsync(string r, int n, string m, CancellationToken c) => throw new NotImplementedException();
    }
}
