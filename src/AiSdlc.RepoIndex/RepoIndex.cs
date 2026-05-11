namespace AiSdlc.RepoIndex;

public sealed record RepoIndex
{
    public required string Repository    { get; init; }
    public required string Description   { get; init; }
    public required StackInfo Stack      { get; init; }
    public IReadOnlyList<PageInfo>     Pages          { get; init; } = [];
    public IReadOnlyList<string>       ApiEndpoints   { get; init; } = [];
    public IReadOnlyList<string>       DatabaseTables { get; init; } = [];
    public IReadOnlyList<string>       HighRiskPaths  { get; init; } = [];
    public IReadOnlyList<string>       MediumRiskPaths{ get; init; } = [];
    public IReadOnlyList<string>       LowRiskPaths   { get; init; } = [];
    public string                      BranchNaming   { get; init; } = string.Empty;
    public DateTimeOffset              IndexedAtUtc   { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record StackInfo
{
    public FrontendInfo? Frontend { get; init; }
    public ApiInfo?      Api      { get; init; }
    public DatabaseInfo? Database { get; init; }
}

public sealed record FrontendInfo
{
    public string Framework { get; init; } = string.Empty;
    public string Language  { get; init; } = string.Empty;
    public string Location  { get; init; } = string.Empty;
}

public sealed record ApiInfo
{
    public string Framework { get; init; } = string.Empty;
    public string Language  { get; init; } = string.Empty;
    public string Location  { get; init; } = string.Empty;
}

public sealed record DatabaseInfo
{
    public string Engine   { get; init; } = string.Empty;
    public string Orm      { get; init; } = string.Empty;
    public string Migrations { get; init; } = string.Empty;
}

public sealed record PageInfo
{
    public string Path        { get; init; } = string.Empty;
    public string Component   { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
