using AiSdlc.Orchestrator.Functions;
using AiSdlc.RepoIndex.Charter;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class CreateBuildIntakeTests
{
    private const string ValidBody = """
        {
          "appId": "test123",
          "ownerRef": "owner-opaque",
          "callbackBaseUrl": "https://ca-yorrixx-dev-api.example/api",
          "charter": {
            "SchemaVersion": 1,
            "Identity": { "AppName": "Sport121", "OneLineDescription": "find coaches" },
            "Audience": { "PrimaryUserDescription": "athletes", "ExpectedScale": "Public" },
            "Purpose": { "ProblemBeingSolved": "hard to find coaches", "SuccessCriteria": ["email a coach"] },
            "Features": [{ "Id": "f1", "Name": "coach grid", "Description": "grid", "Status": "Planned", "AddedIn": "charter", "Priority": "MustHave" }],
            "Constraints": { "DataSensitivity": "Low", "NeedsAuth": false, "NeedsPayments": false, "NeedsEmail": true, "NeedsAIApi": false, "NeedsPersistence": false },
            "Integrations": [],
            "AdditionalContext": ""
          }
        }
        """;

    [Fact]
    public void ParseAndValidate_accepts_a_well_formed_request_with_mixed_casing()
    {
        var (req, error) = CreateBuildFunction.ParseAndValidate(ValidBody);

        Assert.Null(error);
        Assert.NotNull(req);
        Assert.Equal("test123", req!.AppId);
        Assert.Equal("Sport121", req.Charter!.Identity.AppName);
        Assert.True(req.Charter.Constraints.NeedsEmail);
        Assert.False(req.Charter.Constraints.NeedsPersistence);
        Assert.Equal(ExpectedScale.Public, req.Charter.Audience.ExpectedScale); // string enum parsed
    }

    [Theory]
    [InlineData("")]                                                                                  // empty
    [InlineData("not json")]                                                                          // malformed
    [InlineData("{}")]                                                                                // missing everything
    [InlineData("""{"appId":"a","callbackBaseUrl":"https://x/api"}""")]                               // missing charter
    [InlineData("""{"appId":"a","callbackBaseUrl":"not-a-url","charter":{"Identity":{"AppName":"X"}}}""")]    // bad url
    [InlineData("""{"appId":"","callbackBaseUrl":"https://x/api","charter":{"Identity":{"AppName":"X"}}}""")] // empty appId
    [InlineData("""{"appId":"a","callbackBaseUrl":"https://x/api","charter":{"Identity":{"AppName":""}}}""")] // empty AppName
    public void ParseAndValidate_rejects_invalid_requests(string body)
    {
        var (req, error) = CreateBuildFunction.ParseAndValidate(body);
        Assert.Null(req);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(null, null, true)]            // unconfigured → allowed (dev)
    [InlineData("", "", true)]                // unconfigured → allowed
    [InlineData("secret", "secret", true)]    // match
    [InlineData("wrong", "secret", false)]    // mismatch
    [InlineData(null, "secret", false)]       // configured, none provided
    public void IsAuthorized_enforces_the_key_when_configured(string? provided, string? configured, bool expected)
    {
        Assert.Equal(expected, CreateBuildFunction.IsAuthorized(provided, configured));
    }

    [Fact]
    public void BuildInstanceId_is_deterministic_on_appId()
    {
        Assert.Equal("build-abc", CreateBuildFunction.BuildInstanceId("abc"));
    }
}
