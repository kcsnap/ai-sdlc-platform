using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.GitHub;
using Microsoft.Extensions.Logging;

namespace AiSdlc.RepoIndex.Charter;

public sealed class GitHubCharterReader : ICharterReader
{
    public const string CharterPath = ".yorrixx/charter.json";
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly IGitHubService _gitHub;
    private readonly ILogger<GitHubCharterReader> _logger;

    public GitHubCharterReader(IGitHubService gitHub, ILogger<GitHubCharterReader> logger)
    {
        _gitHub = gitHub;
        _logger = logger;
    }

    public async Task<Charter?> ReadAsync(string repository, CancellationToken cancellationToken)
    {
        var json = await _gitHub.GetFileContentAsync(repository, CharterPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        Charter? charter;
        try
        {
            charter = JsonSerializer.Deserialize<Charter>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize {Path} in {Repository}: {Message}",
                CharterPath, repository, ex.Message);
            return null;
        }

        if (charter is null)
            return null;

        if (charter.SchemaVersion != SupportedSchemaVersion)
        {
            _logger.LogWarning(
                "Unrecognised charter SchemaVersion {Version} in {Repository} (supported: {Supported}) — ignoring.",
                charter.SchemaVersion, repository, SupportedSchemaVersion);
            return null;
        }

        return charter;
    }
}
