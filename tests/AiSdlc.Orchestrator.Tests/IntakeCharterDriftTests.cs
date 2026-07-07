using AiSdlc.Orchestrator.Functions;
using Xunit;
using Yorrixx.Contracts.Generation;

namespace AiSdlc.Orchestrator.Tests;

/// <summary>
/// DRIFT TESTS (A4): the create-build intake payload is what yorrixx-app sends live — camelCase property
/// names with NUMERIC enum values (their serializer's default). The platform parses it with the package's
/// Charter types via CreateBuildFunction.ParseAndValidate. If a future Yorrixx.Contracts.Charter release
/// changes a field name, an enum member, or enum NUMBERING, these tests fail CI loudly instead of a live
/// build silently mis-parsing. (The charter.json shape — PascalCase + STRING enums — is pinned separately
/// in AiSdlc.RepoIndex.Tests CharterParseTests.)
/// </summary>
public sealed class IntakeCharterDriftTests
{
    // Representative of the live intake wire shape: camelCase keys, numeric enums, full 32-char appId.
    // Numeric pins (package enum order, NO Unknown sentinel): ExpectedScale Solo=0/SmallTeam=1/Public=2;
    // DataSensitivity Low=0/Medium=1/High=2; FeatureStatus Planned=0/Built=1/Removed=2;
    // FeaturePriority MustHave=0/NiceToHave=1.
    private const string IntakePayload = """
        {
          "appId": "c50cb42440c2462a93a9777a800cc44d",
          "ownerRef": "user_2abCdEfGhIjKlMnOpQrStUvWxYz",
          "callbackBaseUrl": "https://ca-yorrixx-dev-api.example/v1/admin",
          "charter": {
            "schemaVersion": 1,
            "identity": { "appName": "TaskFlow", "oneLineDescription": "Personal task tracker" },
            "audience": { "primaryUserDescription": "Solo developers", "expectedScale": 0 },
            "purpose": { "problemBeingSolved": "I lose track of tasks", "successCriteria": ["Capture in <5s"] },
            "features": [
              { "id": "f1", "name": "Capture task", "description": "Quick-add modal", "status": 0, "addedIn": "charter", "priority": 0 }
            ],
            "constraints": { "dataSensitivity": 2, "needsAuth": true, "needsPayments": false, "needsEmail": false, "needsAIApi": false, "needsPersistence": true },
            "integrations": [ { "name": "Stripe", "purpose": "payments" } ],
            "additionalContext": "Mobile-first."
          }
        }
        """;

    [Fact]
    public void Intake_payload_parses_with_the_fields_the_platform_relies_on()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(IntakePayload);

        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("c50cb42440c2462a93a9777a800cc44d", request!.AppId);
        Assert.Equal("user_2abCdEfGhIjKlMnOpQrStUvWxYz", request.OwnerRef);
        Assert.Equal("https://ca-yorrixx-dev-api.example/v1/admin", request.CallbackBaseUrl);

        var charter = request.Charter!;
        Assert.Equal("TaskFlow", charter.Identity.AppName);
        Assert.Equal(ExpectedScale.Solo, charter.Audience.ExpectedScale);          // numeric 0
        Assert.Equal(DataSensitivity.High, charter.Constraints.DataSensitivity);   // numeric 2
        Assert.Equal(FeatureStatus.Planned, charter.Features[0].Status);           // numeric 0
        Assert.Equal(FeaturePriority.MustHave, charter.Features[0].Priority);      // numeric 0
        Assert.True(charter.Constraints.NeedsAuth);
        Assert.True(charter.Constraints.NeedsPersistence);
        Assert.Equal("Stripe", charter.Integrations[0].Name);

        // The two derived decisions the platform makes from this payload:
        Assert.Equal(StackProfile.FullStack, StackProfiles.Resolve(charter));
        Assert.Equal("user-app-c50cb424", BuildActivityFunctions.RepoName(request.AppId));
    }

    [Fact]
    public void Missing_charter_identity_is_a_clean_400_not_an_NRE()
    {
        var (request, error) = CreateBuildFunction.ParseAndValidate(
            """{ "appId": "x", "callbackBaseUrl": "https://cb.example", "charter": { "schemaVersion": 1 } }""");

        Assert.Null(request);
        Assert.Equal("charter.Identity.AppName is required", error);
    }
}
