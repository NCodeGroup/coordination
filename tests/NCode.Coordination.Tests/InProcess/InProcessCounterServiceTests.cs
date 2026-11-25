// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessCounterServiceTests
{
    private static TimeToLive TwoSeconds { get; } = new(TimeSpan.FromSeconds(2));

    private static TimeToLive ThirtySeconds { get; } = new(TimeSpan.FromSeconds(30));

    #region IncrementAsync Tests

    [Fact]
    public async Task IncrementAsync_WhenTheCountWouldPassTheLargestCount_ThrowsLimitExceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var service = new InProcessCounterService(time);
        var key = InProcessTesting.Keyspace.CreateKey("full");
        await service.IncrementAsync(key, long.MaxValue, ThirtySeconds, null, cancellationToken);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementAsync(key, 1, ThirtySeconds, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task IncrementAsync_WhenTheCountWouldPassTheLargestCount_KeepsTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var service = new InProcessCounterService(time);
        var key = InProcessTesting.Keyspace.CreateKey("full");
        await service.IncrementAsync(key, long.MaxValue, ThirtySeconds, null, cancellationToken);

        await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementAsync(key, 1, ThirtySeconds, null, cancellationToken)
        );

        var count = await service.ReadAsync(key, null, cancellationToken);

        Assert.Equal(long.MaxValue, count);
    }

    #endregion

    #region IncrementBatchAsync Tests

    [Fact]
    public async Task IncrementBatchAsync_WhenACountWouldPassTheLargestCount_ThrowsLimitExceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var service = new InProcessCounterService(time);
        var full = InProcessTesting.Keyspace.CreateKey("full");
        var other = InProcessTesting.Keyspace.CreateKey("other");
        await service.IncrementAsync(full, long.MaxValue, ThirtySeconds, null, cancellationToken);

        var otherIncrement = new CounterIncrement(other, 1, long.MaxValue, ThirtySeconds);
        var fullIncrement = new CounterIncrement(full, 1, long.MaxValue, ThirtySeconds);
        CounterIncrement[] increments = [otherIncrement, fullIncrement];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementBatchAsync(increments, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
    }

    [Fact]
    public async Task IncrementBatchAsync_WhenACountWouldPassTheLargestCount_ChangesNoCounter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var service = new InProcessCounterService(time);
        var full = InProcessTesting.Keyspace.CreateKey("full");
        var other = InProcessTesting.Keyspace.CreateKey("other");
        await service.IncrementAsync(full, long.MaxValue, ThirtySeconds, null, cancellationToken);

        var otherIncrement = new CounterIncrement(other, 1, long.MaxValue, ThirtySeconds);
        var fullIncrement = new CounterIncrement(full, 1, long.MaxValue, ThirtySeconds);
        CounterIncrement[] increments = [otherIncrement, fullIncrement];

        await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementBatchAsync(increments, null, cancellationToken)
        );

        var otherCount = await service.ReadAsync(other, null, cancellationToken);
        var fullCount = await service.ReadAsync(full, null, cancellationToken);

        Assert.Equal(0, otherCount);
        Assert.Equal(long.MaxValue, fullCount);
    }

    [Fact]
    public async Task IncrementBatchAsync_WhenACounterEndsDuringTheBatch_AppliesTheBatchWithoutThrowing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The fake clock moves one second at each read, so a counter's expiry reads the clock the way a real clock
        // moves between the calls of one batch, and the test depends on this order of reads:
        // read 0 starts the counter's 2-second life in IncrementAsync;
        // read 1 is the batch loop's read of the counter, 1 second in, which finds it live;
        // read 2 is the batch's apply read of the same counter, 2 seconds in, which finds it ended.
        var time = InProcessTesting.CreateTime();
        time.AutoAdvanceAmount = TimeSpan.FromSeconds(1);
        var service = new InProcessCounterService(time);
        var first = InProcessTesting.Keyspace.CreateKey("first");
        var second = InProcessTesting.Keyspace.CreateKey("second");
        await service.IncrementAsync(first, 1, TwoSeconds, null, cancellationToken);

        var firstIncrement = new CounterIncrement(first, 1, 10, TwoSeconds);
        var secondIncrement = new CounterIncrement(second, 1, 10, TwoSeconds);
        CounterIncrement[] increments = [firstIncrement, secondIncrement];

        var result = await service.IncrementBatchAsync(increments, null, cancellationToken);

        Assert.Equal([2L, 1L], result.Counts);
        Assert.Null(result.ExceededIndex);
    }

    [Fact]
    public async Task IncrementBatchAsync_WhenACounterEndsDuringTheBatch_RestartsItWithItsFirstIncrementsTimeToLive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The fake clock moves one second at each read, in the order of the test above: the counter's 2-second life
        // starts at read 0, the batch loop finds it live at read 1 and the apply finds it ended at read 2.
        var time = InProcessTesting.CreateTime();
        time.AutoAdvanceAmount = TimeSpan.FromSeconds(1);
        var service = new InProcessCounterService(time);
        var first = InProcessTesting.Keyspace.CreateKey("first");
        var second = InProcessTesting.Keyspace.CreateKey("second");
        await service.IncrementAsync(first, 1, TwoSeconds, null, cancellationToken);

        var firstIncrement = new CounterIncrement(first, 1, 10, ThirtySeconds);
        var secondIncrement = new CounterIncrement(second, 1, 10, TwoSeconds);
        CounterIncrement[] increments = [firstIncrement, secondIncrement];

        await service.IncrementBatchAsync(increments, null, cancellationToken);

        time.AutoAdvanceAmount = TimeSpan.Zero;

        var count = await service.ReadAsync(first, null, cancellationToken);

        // A counter that stayed live through the apply would keep its 2-second expiry from its creation and read 0 by
        // now, so the count of 2 shows the apply restarted the counter with the batch's 30-second time to live.
        Assert.Equal(2, count);
    }

    #endregion
}
