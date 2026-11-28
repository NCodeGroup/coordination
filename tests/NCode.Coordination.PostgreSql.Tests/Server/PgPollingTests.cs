// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgPollingTests
{
    #region UntilAsync Tests

    [Fact]
    public async Task UntilAsync_ConditionFalseThenTrue_WaitsOneIntervalOnTheClock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider();
        var polling = new PgPolling(time);
        var calls = 0;
        var condition = (CancellationToken _) => ValueTask.FromResult(++calls > 1);

        var wait = polling.UntilAsync(condition, cancellationToken).AsTask();

        Assert.False(wait.IsCompleted);
        Assert.Equal(1, calls);

        time.Advance(PgPolling.Interval);

        await wait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(2, calls);
    }

    #endregion
}
