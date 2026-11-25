// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
        var count = await Counters.IncrementAsync(
            CreateKey("c"),
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

        Assert.Equal(7, count);
        Assert.Equal(7, await Counters.ReadAsync(key, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that incrementing by an amount below 1 throws an argument out of range error.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenAmountIsBelowOne_ThrowsArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Counters.IncrementAsync(
                CreateKey("c"),
                0,
                LongTimeToLive,
                null,
                TestCancellationToken
            )
        );
    }

    /// <summary>
    /// Checks that a counter whose time to live has ended reads as zero and starts again from the next amount.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenTimeToLivePasses_StartsFromZero()
    {
        var key = CreateKey("c");
        await Counters.IncrementAsync(key, 3, ShortTimeToLive, null, TestCancellationToken);

        await AdvanceClockAsync(StillAlive);
        await AdvanceClockAsync(PastExpiry);

        Assert.Equal(0, await Counters.ReadAsync(key, null, TestCancellationToken));
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
        await AdvanceClockAsync(StillAlive);
        var count = await Counters.IncrementAsync(
            key,
            1,
            LongTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.Equal(2, count);

        await AdvanceClockAsync(PastExpiry);

        Assert.Equal(0, await Counters.ReadAsync(key, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that incrementing with an option extension that the provider does not know throws an unsupported
    /// error.
    /// </summary>
    [Fact]
    public async Task IncrementAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Counters.IncrementAsync(
                CreateKey("c"),
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
        var count = await Counters.ReadAsync(CreateKey("c"), null, TestCancellationToken);

        Assert.Equal(0, count);
    }

    /// <summary>
    /// Checks that reading with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Counters.ReadAsync(CreateKey("c"), null, new CancellationToken(canceled: true))
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

        Assert.Equal(0, await Counters.ReadAsync(key, null, TestCancellationToken));
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

        var result = await Counters.IncrementBatchAsync(
            [
                new CounterIncrement(key1, 1, 10, LongTimeToLive),
                new CounterIncrement(key2, 2, 10, LongTimeToLive),
            ],
            null,
            TestCancellationToken
        );

        Assert.Equal([1L, 2L], result.Counts);
        Assert.Null(result.ExceededIndex);
        Assert.Equal(1, await Counters.ReadAsync(key1, null, TestCancellationToken));
        Assert.Equal(2, await Counters.ReadAsync(key2, null, TestCancellationToken));
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

        var result = await Counters.IncrementBatchAsync(
            [
                new CounterIncrement(key1, 1, 10, LongTimeToLive),
                new CounterIncrement(key2, 5, 3, LongTimeToLive),
                new CounterIncrement(key3, 1, 10, LongTimeToLive),
            ],
            null,
            TestCancellationToken
        );

        Assert.Equal([1L, 5L], result.Counts);
        Assert.Equal(1, result.ExceededIndex);
        Assert.Equal(1, await Counters.ReadAsync(key1, null, TestCancellationToken));
        Assert.Equal(5, await Counters.ReadAsync(key2, null, TestCancellationToken));
        Assert.Equal(0, await Counters.ReadAsync(key3, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that a batch that names a counter twice applies its increments in order.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenAKeyRepeats_AppliesInOrder()
    {
        var key = CreateKey("k1");

        var result = await Counters.IncrementBatchAsync(
            [
                new CounterIncrement(key, 1, 10, LongTimeToLive),
                new CounterIncrement(key, 2, 10, LongTimeToLive),
            ],
            null,
            TestCancellationToken
        );

        Assert.Equal([1L, 3L], result.Counts);
        Assert.Equal(3, await Counters.ReadAsync(key, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that an empty batch throws an argument error.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenEmpty_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Counters.IncrementBatchAsync([], null, TestCancellationToken)
        );
    }

    /// <summary>
    /// Checks that a batch with more increments than the limit allows throws an argument error.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenTooLarge_ThrowsArgumentException()
    {
        var increments = Enumerable
            .Range(0, CoordinationLimits.MaxBatchIncrements + 1)
            .Select(index => new CounterIncrement(CreateKey($"k{index}"), 1, 10, LongTimeToLive))
            .ToList();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Counters.IncrementBatchAsync(increments, null, TestCancellationToken)
        );
    }

    /// <summary>
    /// Checks that a batch whose increments are in different keyspaces throws an argument error.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenKeyspacesDiffer_ThrowsArgumentException()
    {
        var other = new Keyspace("conformance", KeySegment.FromGuid(Guid.NewGuid()));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Counters.IncrementBatchAsync(
                [
                    new CounterIncrement(CreateKey("k1"), 1, 10, LongTimeToLive),
                    new CounterIncrement(other.CreateKey("k2"), 1, 10, LongTimeToLive),
                ],
                null,
                TestCancellationToken
            )
        );
    }

    /// <summary>
    /// Checks that a provider that declares atomicity across partitions applies the increments of keys in different
    /// partitions together.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenKeysSpanPartitionsAndAtomicityAcrossPartitionsIsDeclared_AppliesEveryIncrement()
    {
        Assert.SkipUnless(
            Declares(Primitives.Counter, Guarantees.AtomicAcrossPartitions),
            "The provider does not declare atomicity across partitions."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };

        var result = await Counters.IncrementBatchAsync(
            [
                new CounterIncrement(key1, 1, 10, LongTimeToLive),
                new CounterIncrement(key2, 2, 10, LongTimeToLive),
            ],
            null,
            TestCancellationToken
        );

        Assert.Equal([1L, 2L], result.Counts);
        Assert.Equal(1, await Counters.ReadAsync(key1, null, TestCancellationToken));
        Assert.Equal(2, await Counters.ReadAsync(key2, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that a provider that declares only atomicity within a partition refuses a batch whose keys are in
    /// different partitions.
    /// </summary>
    [Fact]
    public async Task IncrementBatchAsync_WhenKeysSpanPartitionsAndOnlyAtomicityWithinPartitionIsDeclared_ThrowsUnsupported()
    {
        Assert.SkipUnless(
            Declares(Primitives.Counter, Guarantees.AtomicWithinPartition)
                && !Declares(Primitives.Counter, Guarantees.AtomicAcrossPartitions),
            "The provider does not declare atomicity within a partition only."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Counters.IncrementBatchAsync(
                [
                    new CounterIncrement(key1, 1, 10, LongTimeToLive),
                    new CounterIncrement(key2, 2, 10, LongTimeToLive),
                ],
                null,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion
}
