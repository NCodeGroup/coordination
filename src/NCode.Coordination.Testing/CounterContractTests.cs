// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the counter primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class CounterContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's counter service, and skips the test when the provider does not support counters.
    /// </summary>
    private ICounterService Counters => GetService<ICounterService>(Primitives.Counter);

    #region IncrementAsync Tests

    /// <summary>
    /// Checks that incrementing a counter that does not exist returns the amount.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenCounterIsAbsent_ReturnsTheAmount()
    {
        var key = CreateKey("c");

        var count = await Counters.IncrementAsync(
            key,
            3,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(3, count);
    }

    /// <summary>
    /// Checks that incrementing a counter that exists returns the sum of its count and the amount.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenCounterExists_ReturnsTheSum()
    {
        var key = CreateKey("c");
        await Counters.IncrementAsync(key, 3, LongTimeToLive, null, TestCancellationToken);

        var count = await Counters.IncrementAsync(
            key,
            4,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        var stored = await Counters.ReadAsync(key, null, TestCancellationToken);
        Assert.Equal(7, count);
        Assert.Equal(7, stored);
    }

    /// <summary>
    /// Checks that incrementing by an amount below 1 throws an argument out of range error.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenAmountIsBelowOne_ThrowsArgumentOutOfRangeException()
    {
        var key = CreateKey("c");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Counters.IncrementAsync(key, 0, LongTimeToLive, null, TestCancellationToken)
        );
        Assert.Equal("amount", exception.ParamName);
    }

    /// <summary>
    /// Checks that a counter whose time to live has ended reads as zero and starts again from the next amount.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenTimeToLivePasses_StartsFromZero()
    {
        var key = CreateKey("c");
        await Counters.IncrementAsync(key, 3, ShortTimeToLive, null, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var expired = await Counters.ReadAsync(key, null, TestCancellationToken);
        Assert.Equal(0, expired);
        var count = await Counters.IncrementAsync(
            key,
            4,
            LongTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.Equal(4, count);
    }

    /// <summary>
    /// Checks that an increment of an existing counter leaves the time to live that its creating increment gave it.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenCounterExists_KeepsTheTimeToLiveThatCreatedIt()
    {
        var key = CreateKey("c");
        await Counters.IncrementAsync(key, 1, ShortTimeToLive, null, TestCancellationToken);
        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        var count = await Counters.IncrementAsync(
            key,
            1,
            LongTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.Equal(2, count);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var stored = await Counters.ReadAsync(key, null, TestCancellationToken);
        Assert.Equal(0, stored);
    }

    /// <summary>
    /// Checks that incrementing with an option extension that the provider does not know throws an unsupported
    /// error.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var key = CreateKey("c");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Counters.IncrementAsync(
                key,
                1,
                LongTimeToLive,
                UnknownExtension,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region ReadAsync Tests

    /// <summary>
    /// Checks that reading a counter that does not exist returns zero.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenCounterIsAbsent_ReturnsZero()
    {
        var key = CreateKey("c");

        var count = await Counters.ReadAsync(key, null, TestCancellationToken);

        Assert.Equal(0, count);
    }

    /// <summary>
    /// Checks that reading with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var key = CreateKey("c");
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Counters.ReadAsync(key, null, canceledToken)
        );
    }

    #endregion

    #region ResetAsync Tests

    /// <summary>
    /// Checks that resetting a counter deletes it, so it reads as zero.
    /// </summary>
    [Fact]
    public async Task ResetAsync_WhenCounterExists_DeletesIt()
    {
        var key = CreateKey("c");
        await Counters.IncrementAsync(key, 3, LongTimeToLive, null, TestCancellationToken);

        await Counters.ResetAsync(key, null, TestCancellationToken);

        var count = await Counters.ReadAsync(key, null, TestCancellationToken);
        Assert.Equal(0, count);
    }

    #endregion

    #region IncrementBatchAsync Tests

    /// <summary>
    /// Checks that a batch whose counts stay within their limits applies each increment and reports none as
    /// exceeded.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenNoLimitIsExceeded_AppliesEachIncrement()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");

        var first = new CounterIncrement(key1, 1, 10, LongTimeToLive);
        var second = new CounterIncrement(key2, 2, 10, LongTimeToLive);
        IReadOnlyList<CounterIncrement> increments = [first, second];

        var result = await Counters.IncrementBatchAsync(increments, null, TestCancellationToken);

        var count1 = await Counters.ReadAsync(key1, null, TestCancellationToken);
        var count2 = await Counters.ReadAsync(key2, null, TestCancellationToken);
        Assert.Equal([1L, 2L], result.Counts);
        Assert.Null(result.ExceededIndex);
        Assert.Equal(1, count1);
        Assert.Equal(2, count2);
    }

    /// <summary>
    /// Checks that a batch stops after the first increment whose count exceeds its limit, keeps that increment and
    /// leaves the later ones out.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenAnIncrementExceedsItsLimit_StopsAfterIt()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var key3 = CreateKey("k3");

        var first = new CounterIncrement(key1, 1, 10, LongTimeToLive);
        var second = new CounterIncrement(key2, 5, 3, LongTimeToLive);
        var third = new CounterIncrement(key3, 1, 10, LongTimeToLive);
        IReadOnlyList<CounterIncrement> increments = [first, second, third];

        var result = await Counters.IncrementBatchAsync(increments, null, TestCancellationToken);

        var count1 = await Counters.ReadAsync(key1, null, TestCancellationToken);
        var count2 = await Counters.ReadAsync(key2, null, TestCancellationToken);
        var count3 = await Counters.ReadAsync(key3, null, TestCancellationToken);
        Assert.Equal([1L, 5L], result.Counts);
        Assert.Equal(1, result.ExceededIndex);
        Assert.Equal(1, count1);
        Assert.Equal(5, count2);
        Assert.Equal(0, count3);
    }

    /// <summary>
    /// Checks that a batch that names a counter twice applies its increments in order.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenAKeyRepeats_AppliesInOrder()
    {
        var key = CreateKey("k1");

        var first = new CounterIncrement(key, 1, 10, LongTimeToLive);
        var second = new CounterIncrement(key, 2, 10, LongTimeToLive);
        IReadOnlyList<CounterIncrement> increments = [first, second];

        var result = await Counters.IncrementBatchAsync(increments, null, TestCancellationToken);

        var count = await Counters.ReadAsync(key, null, TestCancellationToken);
        Assert.Equal([1L, 3L], result.Counts);
        Assert.Equal(3, count);
    }

    /// <summary>
    /// Checks that an empty batch throws an argument error that names the increments.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenEmpty_ThrowsArgumentException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Counters.IncrementBatchAsync([], null, TestCancellationToken)
        );

        Assert.Equal("increments", exception.ParamName);
    }

    /// <summary>
    /// Checks that a batch with more increments than the limit allows throws an argument error that names the
    /// increments and applies nothing.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenTooLarge_ThrowsArgumentException()
    {
        var increments = Enumerable
            .Range(0, CoordinationLimits.MaxBatchIncrements + 1)
            .Select(index =>
            {
                var key = CreateKey($"k{index}");
                return new CounterIncrement(key, 1, 10, LongTimeToLive);
            })
            .ToList();

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Counters.IncrementBatchAsync(increments, null, TestCancellationToken)
        );

        var count = await Counters.ReadAsync(increments[0].Key, null, TestCancellationToken);
        Assert.Equal("increments", exception.ParamName);
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Checks that a batch whose increments are in different keyspaces throws an argument error that names the
    /// increments and applies nothing.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenKeyspacesDiffer_ThrowsArgumentException()
    {
        var other = CreateUniqueKeyspace();
        var key1 = CreateKey("k1");
        var key2 = other.CreateKey("k2");
        var first = new CounterIncrement(key1, 1, 10, LongTimeToLive);
        var second = new CounterIncrement(key2, 1, 10, LongTimeToLive);
        IReadOnlyList<CounterIncrement> increments = [first, second];

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Counters.IncrementBatchAsync(increments, null, TestCancellationToken)
        );

        var count = await Counters.ReadAsync(key1, null, TestCancellationToken);
        Assert.Equal("increments", exception.ParamName);
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Checks that an increment whose count reaches its limit exactly is not reported as exceeded.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenACountReachesItsLimit_ReportsNoneExceeded()
    {
        var key = CreateKey("k1");
        var only = new CounterIncrement(key, 3, 3, LongTimeToLive);
        IReadOnlyList<CounterIncrement> increments = [only];

        var result = await Counters.IncrementBatchAsync(increments, null, TestCancellationToken);

        Assert.Equal([3L], result.Counts);
        Assert.Null(result.ExceededIndex);
    }

    /// <summary>
    /// Checks that a batch with the largest number of increments applies each of them.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenItHoldsTheLargestNumberOfIncrements_AppliesEach()
    {
        var increments = Enumerable
            .Range(0, CoordinationLimits.MaxBatchIncrements)
            .Select(index =>
            {
                var key = CreateKey($"k{index}");
                return new CounterIncrement(key, 1, 10, LongTimeToLive);
            })
            .ToList();

        var result = await Counters.IncrementBatchAsync(increments, null, TestCancellationToken);

        var last = await Counters.ReadAsync(increments[^1].Key, null, TestCancellationToken);
        Assert.Equal(CoordinationLimits.MaxBatchIncrements, result.Counts.Length);
        Assert.Null(result.ExceededIndex);
        Assert.Equal(1, last);
    }

    /// <summary>
    /// Checks that a provider that declares atomicity across partitions applies the increments of keys in different
    /// partitions together.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenKeysSpanPartitionsAndAtomicityAcrossPartitionsIsDeclared_AppliesEveryIncrement()
    {
        var declaresAcross = Declares(Primitives.Counter, Guarantees.AtomicAcrossPartitions);
        Assert.SkipUnless(
            declaresAcross,
            "The provider does not declare atomicity across partitions."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };
        var first = new CounterIncrement(key1, 1, 10, LongTimeToLive);
        var second = new CounterIncrement(key2, 2, 10, LongTimeToLive);
        IReadOnlyList<CounterIncrement> increments = [first, second];

        var result = await Counters.IncrementBatchAsync(increments, null, TestCancellationToken);

        var count1 = await Counters.ReadAsync(key1, null, TestCancellationToken);
        var count2 = await Counters.ReadAsync(key2, null, TestCancellationToken);
        Assert.Equal([1L, 2L], result.Counts);
        Assert.Equal(1, count1);
        Assert.Equal(2, count2);
    }

    /// <summary>
    /// Checks that a provider that declares only atomicity within a partition refuses a batch whose keys are in
    /// different partitions.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenKeysSpanPartitionsAndOnlyAtomicityWithinPartitionIsDeclared_ThrowsUnsupported()
    {
        var declaresWithin = Declares(Primitives.Counter, Guarantees.AtomicWithinPartition);
        var declaresAcross = Declares(Primitives.Counter, Guarantees.AtomicAcrossPartitions);
        Assert.SkipUnless(
            declaresWithin && !declaresAcross,
            "The provider does not declare atomicity within a partition only."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };
        var first = new CounterIncrement(key1, 1, 10, LongTimeToLive);
        var second = new CounterIncrement(key2, 2, 10, LongTimeToLive);
        IReadOnlyList<CounterIncrement> increments = [first, second];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Counters.IncrementBatchAsync(increments, null, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that the history of clients that increment, read and reset two counters of one partition, and batch
    /// increments across both, at once is linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenBatchesSpanKeys_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Counter);
        var counters = Counters;
        var first = CreateKey("batch", "a") with { PartitionDepth = 1 };
        var second = CreateKey("batch", "b") with { PartitionDepth = 1 };
        CoordinationKey[] keys = [first, second];
        var recorder = new HistoryRecorder();
        const string partition = "batch";
        const long limit = 6;
        const long firstAmount = 1;
        const long secondAmount = 2;

        await ConcurrentRun.RunAsync(
            async (client, index, cancellationToken) =>
            {
                var k = index % 2;
                var key = keys[k];
                switch ((client + index) % 4)
                {
                    case 0:
                        var amount = client + 1;
                        var increment = new KeyedCall(
                            k,
                            new CounterCall(CounterCall.Increment, amount, 0)
                        );
                        await recorder.RecordAsync(
                            client,
                            partition,
                            increment,
                            token =>
                                counters.IncrementAsync(key, amount, LongTimeToLive, null, token),
                            cancellationToken
                        );
                        break;
                    case 1:
                        var read = new KeyedCall(k, new CounterCall(CounterCall.Read, 0, 0));
                        await recorder.RecordAsync(
                            client,
                            partition,
                            read,
                            token => counters.ReadAsync(key, null, token),
                            cancellationToken
                        );
                        break;
                    case 2:
                        var batch = new CounterBatchCall(firstAmount, secondAmount, limit);
                        IReadOnlyList<CounterIncrement> increments =
                        [
                            new CounterIncrement(first, firstAmount, limit, LongTimeToLive),
                            new CounterIncrement(second, secondAmount, limit, LongTimeToLive),
                        ];
                        await recorder.RecordAsync(
                            client,
                            partition,
                            batch,
                            token => counters.IncrementBatchAsync(increments, null, token),
                            cancellationToken
                        );
                        break;
                    default:
                        var reset = new KeyedCall(k, new CounterCall(CounterCall.Reset, 0, 0));
                        await recorder.RecordAsync(
                            client,
                            partition,
                            reset,
                            token => counters.ResetAsync(key, null, token),
                            cancellationToken
                        );
                        break;
                }
            },
            TestCancellationToken
        );

        var history = recorder.ToHistory();
        HistoryAssert.Linearizable(new CounterBatchModel(), history);
    }

    /// <summary>
    /// Checks that the history of clients that increment, read, batch and reset two counters at once is linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsRace_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Counter);
        var counters = Counters;
        var keys = CreateRunKeys();
        var recorder = new HistoryRecorder();
        const long batchAmount = 2;
        const long batchLimit = 10;

        await ConcurrentRun.RunAsync(
            async (client, index, cancellationToken) =>
            {
                var key = keys[(client + index) % keys.Length];
                var partition = key.ToString();
                switch ((client + index) % 4)
                {
                    case 0:
                        var amount = client + 1;
                        var increment = new CounterCall(CounterCall.Increment, amount, 0);
                        await recorder.RecordAsync(
                            client,
                            partition,
                            increment,
                            token =>
                                counters.IncrementAsync(key, amount, LongTimeToLive, null, token),
                            cancellationToken
                        );
                        break;
                    case 1:
                        var read = new CounterCall(CounterCall.Read, 0, 0);
                        await recorder.RecordAsync(
                            client,
                            partition,
                            read,
                            token => counters.ReadAsync(key, null, token),
                            cancellationToken
                        );
                        break;
                    case 2:
                        var batch = new CounterCall(CounterCall.Batch, batchAmount, batchLimit);
                        IReadOnlyList<CounterIncrement> increments =
                        [
                            new CounterIncrement(key, batchAmount, batchLimit, LongTimeToLive),
                        ];
                        await recorder.RecordAsync(
                            client,
                            partition,
                            batch,
                            token => counters.IncrementBatchAsync(increments, null, token),
                            cancellationToken
                        );
                        break;
                    default:
                        var reset = new CounterCall(CounterCall.Reset, 0, 0);
                        await recorder.RecordAsync(
                            client,
                            partition,
                            reset,
                            token => counters.ResetAsync(key, null, token),
                            cancellationToken
                        );
                        break;
                }
            },
            TestCancellationToken
        );

        var history = recorder.ToHistory();
        HistoryAssert.Linearizable(new CounterModel(), history);
    }

    #endregion
}
