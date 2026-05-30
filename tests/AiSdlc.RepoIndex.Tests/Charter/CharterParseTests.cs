using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.RepoIndex.Charter;
using Xunit;

namespace AiSdlc.RepoIndex.Tests.Charter;

public sealed class CharterParseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private const string CanonicalSampleJson = """
        {
          "SchemaVersion": 1,
          "Identity": { "AppName": "TaskFlow", "OneLineDescription": "Personal task tracker for indie devs" },
          "Audience": { "PrimaryUserDescription": "Solo developers managing their own work", "ExpectedScale": "Solo" },
          "Purpose": {
            "ProblemBeingSolved": "I lose track of tasks across projects",
            "SuccessCriteria": ["I can capture a task in under 5 seconds"]
          },
          "Features": [
            {
              "Id": "f1",
              "Name": "Capture task",
              "Description": "Quick-add modal for new tasks",
              "Status": "Planned",
              "AddedIn": "charter",
              "Priority": "MustHave"
            }
          ],
          "Constraints": {
            "DataSensitivity": "Low",
            "NeedsAuth": true,
            "NeedsPayments": false,
            "NeedsEmail": false,
            "NeedsAIApi": false
          },
          "Integrations": [],
          "AdditionalContext": "Mobile-first; offline important; no social."
        }
        """;

    [Fact]
    public void Parses_canonical_yorrixx_sample()
    {
        var charter = JsonSerializer.Deserialize<AiSdlc.RepoIndex.Charter.Charter>(CanonicalSampleJson, JsonOptions);

        Assert.NotNull(charter);
        Assert.Equal(1, charter!.SchemaVersion);
        Assert.Equal("TaskFlow", charter.Identity.AppName);
        Assert.Equal("Personal task tracker for indie devs", charter.Identity.OneLineDescription);
        Assert.Equal(ExpectedScale.Solo, charter.Audience.ExpectedScale);
        Assert.Equal("Solo developers managing their own work", charter.Audience.PrimaryUserDescription);
        Assert.Equal("I lose track of tasks across projects", charter.Purpose.ProblemBeingSolved);
        Assert.Single(charter.Purpose.SuccessCriteria);
        Assert.Single(charter.Features);
        Assert.Equal(FeatureStatus.Planned, charter.Features[0].Status);
        Assert.Equal(FeaturePriority.MustHave, charter.Features[0].Priority);
        Assert.Equal(DataSensitivity.Low, charter.Constraints.DataSensitivity);
        Assert.True(charter.Constraints.NeedsAuth);
        Assert.False(charter.Constraints.NeedsAIApi);
        Assert.Empty(charter.Integrations);
        Assert.Equal("Mobile-first; offline important; no social.", charter.AdditionalContext);
    }

    [Fact]
    public void Unknown_enum_string_throws_JsonException()
    {
        // Strict by design — GitHubCharterReader catches this and logs.
        var json = CanonicalSampleJson.Replace("\"Solo\"", "\"Galactic\"");
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AiSdlc.RepoIndex.Charter.Charter>(json, JsonOptions));
    }

    [Fact]
    public void Missing_optional_fields_default_safely()
    {
        var minimal = """{ "SchemaVersion": 1, "Identity": { "AppName": "X" } }""";

        var charter = JsonSerializer.Deserialize<AiSdlc.RepoIndex.Charter.Charter>(minimal, JsonOptions);

        Assert.NotNull(charter);
        Assert.Equal("X", charter!.Identity.AppName);
        Assert.Empty(charter.Features);
        Assert.Empty(charter.Integrations);
        Assert.Equal(string.Empty, charter.AdditionalContext);
        Assert.Equal(ExpectedScale.Unknown, charter.Audience.ExpectedScale);
        Assert.Equal(DataSensitivity.Unknown, charter.Constraints.DataSensitivity);
    }
}
