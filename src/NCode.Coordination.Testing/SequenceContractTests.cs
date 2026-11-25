// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the sequence primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class SequenceContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's sequence service, and skips the test when the provider does not support sequences.
    /// </summary>
    private ISequenceService Sequences => GetService<ISequenceService>(Primitives.Sequence);

    #region NextAsync Tests

    /// <summary>
    /// Checks that the first number of a sequence that does not exist is 1.
    /// </summary>
    [Fact]
    public async Task NextAsync_WhenSequenceIsNew_ReturnsOne()
    {
        var key = CreateKey("s");

        var number = await Sequences.NextAsync(key, null, TestCancellationToken);

        Assert.Equal(1, number);
    }

    /// <summary>
    /// Checks that each call to take the next number returns the number after the one before.
    /// </summary>
    [Fact]
    public async Task NextAsync_WhenCalledAgain_ReturnsTheNextNumber()
    {
        var key = CreateKey("s");

        var first = await Sequences.NextAsync(key, null, TestCancellationToken);
        var second = await Sequences.NextAsync(key, null, TestCancellationToken);
        var third = await Sequences.NextAsync(key, null, TestCancellationToken);

        Assert.Equal([1L, 2L, 3L], [first, second, third]);
    }

    /// <summary>
    /// Checks that sequences with different keys count each on its own.
    /// </summary>
    [Fact]
    public async Task NextAsync_WhenSequencesDiffer_CountsEachOnItsOwn()
    {
        var first = CreateKey("s", "a");
        var second = CreateKey("s", "b");
        await Sequences.NextAsync(first, null, TestCancellationToken);
        await Sequences.NextAsync(first, null, TestCancellationToken);

        var number = await Sequences.NextAsync(second, null, TestCancellationToken);

        Assert.Equal(1, number);
    }

    /// <summary>
    /// Checks that taking the next number with an option extension that the provider does not know throws an
    /// unsupported error.
    /// </summary>
    [Fact]
    public async Task NextAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var key = CreateKey("s");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Sequences.NextAsync(key, UnknownExtension, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    /// <summary>
    /// Checks that taking the next number with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task NextAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var key = CreateKey("s");
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Sequences.NextAsync(key, null, canceledToken)
        );
    }

    #endregion

    #region ReserveAsync Tests

    /// <summary>
    /// Checks that a block reserved from a sequence that does not exist starts at 1.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_WhenSequenceIsNew_StartsAtOne()
    {
        var key = CreateKey("s");

        var block = await Sequences.ReserveAsync(key, 5, null, TestCancellationToken);

        Assert.Equal(1, block.First);
        Assert.Equal(5, block.Count);
    }

    /// <summary>
    /// Checks that a block reserved after a number was taken starts after that number, and the next number follows the
    /// block.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_AfterNext_StartsAfterTheLastNumber()
    {
        var key = CreateKey("s");
        await Sequences.NextAsync(key, null, TestCancellationToken);

        var block = await Sequences.ReserveAsync(key, 3, null, TestCancellationToken);
        var next = await Sequences.NextAsync(key, null, TestCancellationToken);

        Assert.Equal(2, block.First);
        Assert.Equal(3, block.Count);
        Assert.Equal(4, block.Last);
        Assert.Equal(5, next);
    }

    /// <summary>
    /// Checks that reserving a block of zero numbers throws an argument out of range error.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_WhenCountIsZero_ThrowsArgumentOutOfRangeException()
    {
        var key = CreateKey("s");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Sequences.ReserveAsync(key, 0, null, TestCancellationToken)
        );
        Assert.Equal("count", exception.ParamName);
    }

    /// <summary>
    /// Checks that reserving a block above the largest block throws an argument out of range error.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_WhenCountIsAboveTheLimit_ThrowsArgumentOutOfRangeException()
    {
        var key = CreateKey("s");
        var count = CoordinationLimits.MaxSequenceBlock + 1;

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Sequences.ReserveAsync(key, count, null, TestCancellationToken)
        );
        Assert.Equal("count", exception.ParamName);
    }

    #endregion

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that the history of clients that take the next number and reserve blocks from two sequences at once is
    /// linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsRace_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Sequence);
        var sequences = Sequences;
        var keys = CreateRunKeys();
        var recorder = new HistoryRecorder();

        await ConcurrentRun.RunAsync(
            async (client, index, cancellationToken) =>
            {
                var key = keys[(client + index) % keys.Length];
                var partition = key.ToString();
                if ((client + index) % 3 == 2)
                {
                    var reserve = new SequenceCall(SequenceCall.Reserve, 2);
                    await recorder.RecordAsync(
                        client,
                        partition,
                        reserve,
                        token => sequences.ReserveAsync(key, 2, null, token),
                        cancellationToken
                    );
                }
                else
                {
                    var next = new SequenceCall(SequenceCall.Next, 1);
                    await recorder.RecordAsync(
                        client,
                        partition,
                        next,
                        token => sequences.NextAsync(key, null, token),
                        cancellationToken
                    );
                }
            },
            TestCancellationToken
        );

        HistoryAssert.Linearizable(new SequenceModel(), recorder.ToHistory());
    }

    #endregion
}
