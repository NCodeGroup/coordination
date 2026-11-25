// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class DroppingConnectionTests
{
    #region CallAsync Tests

    [Fact]
    public async Task CallAsync_WhenNotDropped_ReturnsResult()
    {
        var connection = new DroppingConnection();

        var result = await connection.CallAsync(
            _ => ValueTask.FromResult(7),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(7, result);
    }

    [Fact]
    public async Task CallAsync_WhenDropped_FailsThreeCallsAndRunsOnlyTheSecond()
    {
        var connection = new DroppingConnection();
        var runs = 0;
        ValueTask<int> Call(CancellationToken token)
        {
            runs++;
            return ValueTask.FromResult(7);
        }

        await connection.DropAsync(TestContext.Current.CancellationToken);

        var first = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.CallAsync(Call, TestContext.Current.CancellationToken)
        );
        var runsAfterFirst = runs;
        var second = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.CallAsync(Call, TestContext.Current.CancellationToken)
        );
        var runsAfterSecond = runs;
        var third = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.CallAsync(Call, TestContext.Current.CancellationToken)
        );
        var runsAfterThird = runs;
        var fourth = await connection.CallAsync(Call, TestContext.Current.CancellationToken);

        foreach (var failure in new[] { first, second, third })
        {
            Assert.True(failure.OutcomeUnknown);
            Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
        }

        Assert.Equal(0, runsAfterFirst);
        Assert.Equal(1, runsAfterSecond);
        Assert.Equal(1, runsAfterThird);
        Assert.Equal(7, fourth);
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task CallAsync_WhenDroppedAndCallReturnsNothing_FailsThreeCallsAndRunsOnlyTheSecond()
    {
        var connection = new DroppingConnection();
        var runs = 0;
        ValueTask Call(CancellationToken token)
        {
            runs++;
            return ValueTask.CompletedTask;
        }

        await connection.DropAsync(TestContext.Current.CancellationToken);

        var first = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.CallAsync(Call, TestContext.Current.CancellationToken)
        );
        var runsAfterFirst = runs;
        var second = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.CallAsync(Call, TestContext.Current.CancellationToken)
        );
        var runsAfterSecond = runs;
        var third = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.CallAsync(Call, TestContext.Current.CancellationToken)
        );
        var runsAfterThird = runs;
        await connection.CallAsync(Call, TestContext.Current.CancellationToken);

        foreach (var failure in new[] { first, second, third })
        {
            Assert.True(failure.OutcomeUnknown);
            Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
        }

        Assert.Equal(0, runsAfterFirst);
        Assert.Equal(1, runsAfterSecond);
        Assert.Equal(1, runsAfterThird);
        Assert.Equal(2, runs);
    }

    #endregion

    #region Subscribe Tests

    [Fact]
    public async Task Subscribe_WhenDropped_TakesOneDropThenNone()
    {
        var connection = new DroppingConnection();
        var subscription = connection.Subscribe();

        await connection.DropAsync(TestContext.Current.CancellationToken);

        var first = subscription.TakeDrop();
        var second = subscription.TakeDrop();

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task Unsubscribe_WhenDropped_TakesNoDrop()
    {
        var connection = new DroppingConnection();
        var subscription = connection.Subscribe();

        connection.Unsubscribe(subscription);
        await connection.DropAsync(TestContext.Current.CancellationToken);

        Assert.False(subscription.TakeDrop());
    }

    #endregion
}
