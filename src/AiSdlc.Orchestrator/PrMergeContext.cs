using AiSdlc.GitHub;

namespace AiSdlc.Orchestrator;

public sealed record PrMergeContext(
    int PullRequestNumber,
    string HeadSha,
    bool Mergeable,
    bool AllChecksPass,
    bool HasTestCoverage,
    IReadOnlyList<ChangedFile> ChangedFiles);
