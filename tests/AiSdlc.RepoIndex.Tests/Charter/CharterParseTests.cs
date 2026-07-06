using System.Text.Json;
using System.Text.Json.Serialization;
using AiSdlc.RepoIndex.Charter;
using Yorrixx.Contracts.Generation;
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
        var charter = JsonSerializer.Deserialize<Yorrixx.Contracts.Generation.Charter>(CanonicalSampleJson, JsonOptions);

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
            JsonSerializer.Deserialize<Yorrixx.Contracts.Generation.Charter>(json, JsonOptions));
    }

    // DRIFT PIN — package semantics for a minimal charter differ from the old hand-mirror: the package
    // records are positional, so sections absent from the JSON deserialize to NULL (not empty defaults),
    // and the enums have no Unknown sentinel. Anything consuming a charter must treat sections as
    // potentially null unless the producer guarantees the full shape (yorrixx-app serializes complete
    // objects, so full-shape is the wire norm — see Parses_canonical_yorrixx_sample above).
    [Fact]
    public void Minimal_charter_deserializes_with_null_sections()
    {
        var minimal = """{ "SchemaVersion": 1, "Identity": { "AppName": "X" } }""";

        var charter = JsonSerializer.Deserialize<Yorrixx.Contracts.Generation.Charter>(minimal, JsonOptions);

        Assert.NotNull(charter);
        Assert.Equal(1, charter!.SchemaVersion);
        Assert.Equal("X", charter.Identity.AppName);
        Assert.Null(charter.Audience);
        Assert.Null(charter.Constraints);
        Assert.Null(charter.Features);
        Assert.Null(charter.Integrations);
    }
}
