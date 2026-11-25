// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the occurrence primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class OccurrenceContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's occurrence service, and skips the test when the provider does not support occurrences.
    /// </summary>
    private IOccurrenceService Occurrences => GetService<IOccurrenceService>(Primitives.Occurrence);

    #region TryRecordAsync Tests

    /// <summary>
    /// Checks that recording a key that was never recorded returns recorded.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenKeyIsNew_ReturnsRecorded()
    {
        var status = await Occurrences.TryRecordAsync(
            CreateKey("a"),
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(OccurrenceStatus.Recorded, status);
    }

    /// <summary>
    /// Checks that recording a key that was already recorded returns duplicate.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenKeyWasRecorded_ReturnsDuplicate()
    {
        var key = CreateKey("a");
        await Occurrences.TryRecordAsync(key, LongTimeToLive, null, TestCancellationToken);

        var status = await Occurrences.TryRecordAsync(
            key,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(OccurrenceStatus.Duplicate, status);
    }

    /// <summary>
    /// Checks that a key whose time to live has not passed is still a duplicate.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenTimeToLiveHasNotPassed_ReturnsDuplicate()
    {
        var key = CreateKey("a");
        var first = await Occurrences.TryRecordAsync(
            key,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.Equal(OccurrenceStatus.Recorded, first);

        await AdvanceClockAsync(StillAlive);

        var status = await Occurrences.TryRecordAsync(
            key,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(OccurrenceStatus.Duplicate, status);
    }

    /// <summary>
    /// Checks that a key whose time to live passed is recorded again.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenTimeToLivePassed_ReturnsRecordedAgain()
    {
        var key = CreateKey("a");
        var first = await Occurrences.TryRecordAsync(
            key,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );
        Assert.Equal(OccurrenceStatus.Recorded, first);

        await AdvanceClockAsync(StillAlive);
        await AdvanceClockAsync(PastExpiry);

        var status = await Occurrences.TryRecordAsync(
            key,
            ShortTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(OccurrenceStatus.Recorded, status);
    }

    /// <summary>
    /// Checks that keys that differ are recorded independently of each other.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenKeysDiffer_RecordsEach()
    {
        var first = await Occurrences.TryRecordAsync(
            CreateKey("a"),
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        var second = await Occurrences.TryRecordAsync(
            CreateKey("b"),
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(OccurrenceStatus.Recorded, first);
        Assert.Equal(OccurrenceStatus.Recorded, second);
    }

    /// <summary>
    /// Checks that keys with the same segments and different partition depths are recorded independently of each
    /// other.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenPartitionDepthsDiffer_RecordsEach()
    {
        var unpartitioned = CreateKey("a", "b");
        var partitioned = unpartitioned with { PartitionDepth = 1 };

        var first = await Occurrences.TryRecordAsync(
            unpartitioned,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        var second = await Occurrences.TryRecordAsync(
            partitioned,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        Assert.Equal(OccurrenceStatus.Recorded, first);
        Assert.Equal(OccurrenceStatus.Recorded, second);
    }

    /// <summary>
    /// Checks that recording with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Occurrences.TryRecordAsync(
                CreateKey("a"),
                LongTimeToLive,
                UnknownExtension,
                TestCancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    /// <summary>
    /// Checks that recording with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task TryRecordAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Occurrences.TryRecordAsync(
                CreateKey("a"),
                LongTimeToLive,
                null,
                new CancellationToken(canceled: true)
            )
        );
    }

    #endregion
}
