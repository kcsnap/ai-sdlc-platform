using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yorrixx.Provisioner.Contracts;

namespace Yorrixx.Provisioner.Internal;

public sealed class PlatformCallbackOptions
{
    public const string SectionName = "Platform";

    /// Absolute URL of the platform's provision-result callback (Call 2),
    /// e.g. https://func-aisdlc-dev…/api/provision-result.
    public string ProvisionResultUrl { get; init; } = "";

    /// Key the provisioner presents on the callback (X-Provisioner-Key). The
    /// platform validates it. Sourced from Key Vault.
    public string CallbackKey { get; init; } = "";
}

/// Posts Call 2 (provision-result) back to the platform. Best-effort: the
/// platform also polls GET /provision/{buildId}, so a dropped callback can't
/// wedge a run — we log and rely on the poll.
public sealed class PlatformCallbackClient(
    HttpClient http,
    IOptions<PlatformCallbackOptions> options,
    ILogger<PlatformCallbackClient> logger)
{
    private readonly PlatformCallbackOptions _opts = options.Value;

    public async Task PostResultAsync(ProvisionResult result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ProvisionResultUrl))
        {
            logger.LogWarning(
                "Platform:ProvisionResultUrl not configured — skipping provision-result callback for buildId={BuildId}; platform must poll",
                result.BuildId);
            return;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ProvisionResultUrl)
            {
                Content = JsonContent.Create(result),
            };
            if (!string.IsNullOrWhiteSpace(_opts.CallbackKey))
                req.Headers.Add("X-Provisioner-Key", _opts.CallbackKey);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogError("provision-result callback failed status={Status} buildId={BuildId} — platform will poll",
                    (int)resp.StatusCode, result.BuildId);
            }
            else
            {
                logger.LogInformation("provision-result callback ok buildId={BuildId} outcome={Outcome}",
                    result.BuildId, result.Outcome);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "provision-result callback threw buildId={BuildId} — platform will poll", result.BuildId);
        }
    }
}
