// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
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
        var key = CreateKey("a");

        var status = await Occurrences.TryRecordAsync(
            key,
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

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

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

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

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
        var firstKey = CreateKey("a");
        var secondKey = CreateKey("b");

        var first = await Occurrences.TryRecordAsync(
            firstKey,
            LongTimeToLive,
            null,
            TestCancellationToken
        );

        var second = await Occurrences.TryRecordAsync(
            secondKey,
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
        var key = CreateKey("a");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Occurrences.TryRecordAsync(
                key,
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
        var key = CreateKey("a");
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Occurrences.TryRecordAsync(key, LongTimeToLive, null, canceledToken)
        );
    }

    #endregion

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that the history of clients that record two occurrences at once is linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsRace_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Occurrence);
        var occurrences = Occurrences;
        var keys = CreateRunKeys();
        var recorder = new HistoryRecorder();

        await ConcurrentRun.RunAsync(
            async (client, index, cancellationToken) =>
            {
                var key = keys[(client + index) % keys.Length];
                var call = new OccurrenceCall(OccurrenceCall.Record);
                var partition = key.ToString();
                await recorder.RecordAsync(
                    client,
                    partition,
                    call,
                    token => occurrences.TryRecordAsync(key, LongTimeToLive, null, token),
                    cancellationToken
                );
            },
            TestCancellationToken
        );

        var history = recorder.ToHistory();
        HistoryAssert.Linearizable(new OccurrenceModel(), history);
    }

    #endregion
}
