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
