using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AiSdlc.RepoIndex;

// Maps to .ai-sdlc.yml in the target application repo.
public sealed class AiSdlcConfig
{
    public RepoSection?        Repo       { get; set; }
    public StackSection?       Stack      { get; set; }
    public RiskSection?        RiskAreas  { get; set; }
    public AutomationSection?  Automation { get; set; }

    [YamlMember(Alias = "branch_naming")]
    public string? BranchNaming { get; set; }

    public sealed class AutomationSection
    {
        public bool AllowLowRiskAutoMerge        { get; set; }
        public bool AllowLowRiskProductionDeploy { get; set; }
    }

    public sealed class RepoSection
    {
        public string? Name        { get; set; }
        public string? Description { get; set; }
        public string? Owner       { get; set; }
    }

    public sealed class StackSection
    {
        public FrontendSection? Frontend { get; set; }
        public ApiSection?      Api      { get; set; }
        public DatabaseSection? Database { get; set; }
    }

    public sealed class FrontendSection
    {
        public string?       Framework { get; set; }
        public string?       Language  { get; set; }
        public string?       Location  { get; set; }
        public List<PageSection> Pages { get; set; } = [];
    }

    public sealed class PageSection
    {
        public string? Path        { get; set; }
        public string? Component   { get; set; }
        public string? Description { get; set; }
    }

    public sealed class ApiSection
    {
        public string?       Framework { get; set; }
        public string?       Language  { get; set; }
        public string?       Location  { get; set; }
        public List<string>  Endpoints { get; set; } = [];
    }

    public sealed class DatabaseSection
    {
        public string?      Engine     { get; set; }
        public string?      Orm        { get; set; }
        public string?      Migrations { get; set; }
        public List<string> Tables     { get; set; } = [];
    }

    public sealed class RiskSection
    {
        public List<string> High   { get; set; } = [];
        public List<string> Medium { get; set; } = [];
        public List<string> Low    { get; set; } = [];
    }

    public static AiSdlcConfig? Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<AiSdlcConfig?>(yaml);
    }
}
