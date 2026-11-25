// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessCounterServiceTests
{
    private static Keyspace Keyspace { get; } = new("test");

    private static TimeToLive TwoSeconds { get; } = new(TimeSpan.FromSeconds(2));

    #region IncrementBatchAsync Tests

    [Fact]
    public async Task IncrementBatchAsync_WhenACounterEndsDuringTheBatch_AppliesTheBatchWithoutThrowing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new SteppingTimeProvider();
        var service = new InProcessCounterService(time);
        var first = Keyspace.CreateKey("first");
        var second = Keyspace.CreateKey("second");
        await service.IncrementAsync(first, 1, TwoSeconds, null, cancellationToken);

        var firstIncrement = new CounterIncrement(first, 1, 10, TwoSeconds);
        var secondIncrement = new CounterIncrement(second, 1, 10, TwoSeconds);
        CounterIncrement[] increments = [firstIncrement, secondIncrement];

        var result = await service.IncrementBatchAsync(increments, null, cancellationToken);

        Assert.Equal([2L, 1L], result.Counts);
        Assert.Null(result.ExceededIndex);
    }

    #endregion

    private sealed class SteppingTimeProvider : TimeProvider
    {
        /// <summary>
        /// Gets the number of timestamps read so far.
        /// </summary>
        private long Reads { get; set; }

        public override long TimestampFrequency => 1;

        // The clock moves one second at each read, so a counter's expiry reads the clock the way a real clock moves
        // between the calls of one batch:
        // read 0 starts the counter's 2-second life in IncrementAsync;
        // read 1 is the batch loop's read of the counter, 1 second in, which finds it live;
        // read 2 is the batch's apply read of the same counter, 2 seconds in, which finds it ended.
        public override long GetTimestamp()
        {
            return Reads++;
        }
    }
}
