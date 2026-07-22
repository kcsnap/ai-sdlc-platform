using Azure;

namespace Yorrixx.Modules.Hosting;

/// <summary>
/// D15: convergence helpers for ARM operations racing themselves. HealthyChicken's storage create was
/// ACCEPTED by ARM but ran slowly; the SDK's transient re-PUT hit 409 StorageAccountOperationInProgress,
/// the worker failed the build — and the original operation completed 11 minutes later regardless. An
/// in-progress operation on a resource WE name deterministically is ours: the correct move is to wait
/// for it and adopt the result, never to fail.
/// </summary>
public static class ProvisioningRetry
{
    /// <summary>The exact self-race conflict: another (our own) operation holds the resource.</summary>
    public static bool IsOperationInProgress(RequestFailedException ex) =>
        ex.Status == 409
        && string.Equals(ex.ErrorCode, "StorageAccountOperationInProgress", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Polls <paramref name="fetch"/> until it yields the resource the in-flight operation is creating,
    /// or the attempt budget runs out (null — the caller decides how to fail). Delay honours the Azure
    /// Retry-After guidance (the observed 409 carried Retry-After: 40).
    /// </summary>
    public static async Task<T?> PollForResourceAsync<T>(
        Func<CancellationToken, Task<T?>> fetch, int attempts, TimeSpan delay, CancellationToken ct)
        where T : class
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var resource = await fetch(ct);
            if (resource is not null) return resource;
            if (attempt < attempts - 1)
                await Task.Delay(delay, ct);
        }
        return null;
    }
}

/// <summary>
/// D16: wire-appropriate failure detail. RequestFailedException.Message is a multi-line diagnostic dump
/// (status line, error code, headers) — the app owner saw an EMPTY red error box because the raw text
/// never survived to their screen. Callback detail must be one human-readable line, never empty.
/// </summary>
public static class FailureDetail
{
    private const int MaxLength = 300;

    public static string Sanitize(Exception ex)
    {
        // Known Azure conflicts get owner-appropriate text instead of ARM diagnostics.
        if (ex is RequestFailedException { } rfe && ProvisioningRetry.IsOperationInProgress(rfe))
            return "Provisioning conflicted with an in-progress Azure operation on the app's storage account; a retry converges once it completes.";

        var firstLine = (ex.Message ?? string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

        var line = string.IsNullOrWhiteSpace(firstLine)
            ? $"Provisioning failed ({ex.GetType().Name})."
            : firstLine!;

        return line.Length <= MaxLength ? line : line[..MaxLength];
    }
}
