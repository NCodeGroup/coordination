// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespPollingTests
{
    #region UntilAsync Tests

    [Fact]
    public async Task UntilAsync_WhenTheConditionHoldsOnTheThirdCheck_CompletesAfterIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var calls = 0;

        await RespPolling.UntilAsync(
            _ =>
            {
                calls++;
                return ValueTask.FromResult(calls == 3);
            },
            cancellationToken
        );

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task UntilAsync_WhenTheTokenIsCanceled_ThrowsOperationCanceledException()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var delay = TimeSpan.FromMilliseconds(50);
        source.CancelAfter(delay);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await RespPolling.UntilAsync(_ => ValueTask.FromResult(false), source.Token)
        );
    }

    #endregion
}
