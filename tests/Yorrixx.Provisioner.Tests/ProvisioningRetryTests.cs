using Azure;
using Yorrixx.Modules.Hosting;
using Xunit;

namespace Yorrixx.Provisioner.Tests;

/// <summary>
/// D15 (TDD red-first): HealthyChicken failed twice on Azure 409 StorageAccountOperationInProgress —
/// the SDK's transient re-PUT collided with its own accepted (but slow) create, the worker threw, and
/// the original operation completed 11 minutes later anyway. Waiting converges; failing was always wrong.
/// </summary>
public sealed class ProvisioningRetryTests
{
    [Theory]
    [InlineData(409, "StorageAccountOperationInProgress", true)]
    [InlineData(409, "SomeOtherConflict", false)]  // e.g. name taken by another tenant — do NOT wait
    [InlineData(429, "StorageAccountOperationInProgress", false)]
    [InlineData(404, null, false)]
    public void IsOperationInProgress_matches_the_exact_conflict_class(int status, string? code, bool expected)
    {
        var ex = new RequestFailedException(status, "An operation is currently performing on this storage account.", code, null);

        Assert.Equal(expected, ProvisioningRetry.IsOperationInProgress(ex));
    }

    [Fact]
    public async Task PollForResourceAsync_returns_the_resource_once_the_inflight_operation_completes()
    {
        var calls = 0;
        var result = await ProvisioningRetry.PollForResourceAsync<string>(
            _ => Task.FromResult<string?>(++calls < 3 ? null : "the-account"),
            attempts: 5, delay: TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("the-account", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task PollForResourceAsync_returns_null_when_the_operation_never_materializes()
    {
        var result = await ProvisioningRetry.PollForResourceAsync<string>(
            _ => Task.FromResult<string?>(null),
            attempts: 3, delay: TimeSpan.Zero, CancellationToken.None);

        Assert.Null(result);
    }
}

/// <summary>
/// D16 (TDD red-first): the failed build's callback detail was unusable — RequestFailedException.Message
/// is a multi-line dump (status line, error code, headers) and the owner saw an empty red error box.
/// The wire detail must be single-line, human-appropriate, and never empty.
/// </summary>
public sealed class FailureDetailTests
{
    [Fact]
    public void Sanitize_turns_the_azure_409_dump_into_one_human_line()
    {
        var raw = new RequestFailedException(409,
            "An operation is currently performing on this storage account that requires exclusive access.\n" +
            "Status: 409 (Conflict)\nErrorCode: StorageAccountOperationInProgress\n\nHeaders:\nRetry-After: 40\nx-ms-request-id: abc",
            "StorageAccountOperationInProgress", null);

        var detail = FailureDetail.Sanitize(raw);

        Assert.DoesNotContain("\n", detail);
        Assert.DoesNotContain("x-ms-request-id", detail);
        Assert.Contains("storage", detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(detail.Length is > 0 and <= 300);
    }

    [Fact]
    public void Sanitize_never_returns_empty()
    {
        Assert.False(string.IsNullOrWhiteSpace(FailureDetail.Sanitize(new InvalidOperationException(""))));
        Assert.False(string.IsNullOrWhiteSpace(FailureDetail.Sanitize(new Exception("   "))));
    }

    [Fact]
    public void Sanitize_keeps_ordinary_single_line_messages_intact()
    {
        var detail = FailureDetail.Sanitize(new InvalidOperationException("Clerk org creation returned 400 organization_creator_not_found."));

        Assert.Equal("Clerk org creation returned 400 organization_creator_not_found.", detail);
    }
}
