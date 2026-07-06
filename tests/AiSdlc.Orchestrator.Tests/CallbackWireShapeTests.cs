using System.Text.Json;
using AiSdlc.Contracts.Callbacks;
using AiSdlc.Orchestrator.Functions;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

/// <summary>
/// GOLDEN TESTS (A11): the named contract records must serialize BYTE-IDENTICAL to the anonymous objects
/// the platform sent before A11 — same options instance the sender uses (NewAppBuildOrchestrator.CallbackJson:
/// camelCase, nulls omitted), property order = declaration order. Expected strings below are the pre-A11
/// wire shapes verbatim; any change to these payloads is a contract change and must fail here first.
/// </summary>
public sealed class CallbackWireShapeTests
{
    private static string Wire(object payload) => JsonSerializer.Serialize(payload, NewAppBuildOrchestrator.CallbackJson);

    [Fact]
    public void Status_minimal_matches_pre_A11_wire()
    {
        Assert.Equal("""{"status":"queued"}""", Wire(new StatusCallback(CanonicalBuildStatus.Queued)));
    }

    [Fact]
    public void Status_full_matches_pre_A11_wire()
    {
        Assert.Equal(
            """{"status":"failed","phase":"Provision","detail":"boom"}""",
            Wire(new StatusCallback(CanonicalBuildStatus.Failed, "Provision", "boom")));
    }

    [Fact]
    public void Runtime_matches_pre_A11_wire_including_repoUrl()
    {
        Assert.Equal(
            """{"repoUrl":"https://github.com/yorrixx-apps/user-app-c50cb424","hostedUrl":"https://app-x-frontend.azurewebsites.net"}""",
            Wire(new RuntimeCallback("https://github.com/yorrixx-apps/user-app-c50cb424", "https://app-x-frontend.azurewebsites.net")));
    }

    [Fact]
    public void Runtime_omits_null_hostedUrl()
    {
        Assert.Equal(
            """{"repoUrl":"https://github.com/yorrixx-apps/user-app-c50cb424"}""",
            Wire(new RuntimeCallback("https://github.com/yorrixx-apps/user-app-c50cb424", null)));
    }

    [Fact]
    public void Verification_matches_pre_A11_wire()
    {
        var payload = new VerificationCallback("passed", 1,
        [
            new VerificationCheck("deploy-run-green", "Deploy workflow green", "pass", "run 42 success", "2026-07-06T12:00:00.0000000Z"),
            new VerificationCheck("frontend-serves-app", "Hosted URL serves", "pass", null, "2026-07-06T12:00:00.0000000Z")
        ]);

        Assert.Equal(
            """{"outcome":"passed","attempt":1,"checks":[{"checkId":"deploy-run-green","name":"Deploy workflow green","status":"pass","evidence":"run 42 success","at":"2026-07-06T12:00:00.0000000Z"},{"checkId":"frontend-serves-app","name":"Hosted URL serves","status":"pass","at":"2026-07-06T12:00:00.0000000Z"}]}""",
            Wire(payload));
    }

    [Fact]
    public void Cost_matches_pre_A11_wire()
    {
        var payload = new BuildCostCallback
        {
            Model = "claude-haiku-4-5-20251001",
            Phase = "code-gen",
            Iteration = 1,
            InputTokens = 1200,
            OutputTokens = 800,
            CacheReadTokens = 4096,
            CacheWriteTokens = 0,
            RequestId = "req_abc"
        };

        Assert.Equal(
            """{"model":"claude-haiku-4-5-20251001","phase":"code-gen","iteration":1,"inputTokens":1200,"outputTokens":800,"cacheReadTokens":4096,"cacheWriteTokens":0,"calls":1,"requestId":"req_abc"}""",
            Wire(payload));
    }

    [Fact]
    public void Canonical_vocabulary_is_the_ratified_nine()
    {
        Assert.Equal(
            ["queued", "provisioning", "building", "verifying", "ready-for-review", "live", "failed", "deleting", "archived"],
            CanonicalBuildStatus.All);
    }
}
