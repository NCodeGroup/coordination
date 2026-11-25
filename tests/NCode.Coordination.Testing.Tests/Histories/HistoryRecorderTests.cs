// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Histories.Tests;

public sealed class HistoryRecorderTests
{
    private const string Partition = "key";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #region RecordAsync Tests

    [Fact]
    public async ValueTask RecordAsync_WhenCallReturns_RecordsReturnedWithOutput()
    {
        const string input = "get";
        var recorder = new HistoryRecorder();

        var operation = await recorder.RecordAsync(
            2,
            Partition,
            input,
            _ => ValueTask.FromResult(42),
            Token
        );

        Assert.Equal(HistoryOutcome.Returned, operation.Outcome);
        Assert.Equal(42, operation.Output);
        Assert.Equal(2, operation.Client);
        Assert.Equal(Partition, operation.Partition);
        Assert.Equal(input, operation.Input);
        Assert.True(operation.Invoked < operation.Responded);
        var history = recorder.ToHistory();
        var recorded = Assert.Single(history);
        Assert.Equal(operation, recorded);
    }

    [Fact]
    public async ValueTask RecordAsync_WhenCallReturnsNothing_RecordsReturnedWithoutOutput()
    {
        var recorder = new HistoryRecorder();

        var operation = await recorder.RecordAsync(
            0,
            Partition,
            "set",
            _ => ValueTask.CompletedTask,
            Token
        );

        Assert.Equal(HistoryOutcome.Returned, operation.Outcome);
        Assert.Null(operation.Output);
    }

    [Fact]
    public async ValueTask RecordAsync_WhenFailureTookNoEffect_RecordsFailed()
    {
        var exception = new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            false,
            "down"
        );
        var recorder = new HistoryRecorder();

        var operation = await recorder.RecordAsync<int>(
            0,
            Partition,
            "set",
            _ => ValueTask.FromException<int>(exception),
            Token
        );

        Assert.Equal(HistoryOutcome.Failed, operation.Outcome);
        Assert.False(operation.IsIndeterminate);
        Assert.Same(exception, operation.Output);
        Assert.True(operation.Responded < long.MaxValue);
    }

    [Fact]
    public async ValueTask RecordAsync_WhenFailureMayHaveApplied_RecordsIndeterminate()
    {
        var exception = new CoordinationException(CoordinationErrorCodes.Unavailable, true, "lost");
        var recorder = new HistoryRecorder();

        var operation = await recorder.RecordAsync<int>(
            0,
            Partition,
            "set",
            _ => ValueTask.FromException<int>(exception),
            Token
        );

        Assert.Equal(HistoryOutcome.Indeterminate, operation.Outcome);
        Assert.True(operation.IsIndeterminate);
        Assert.Same(exception, operation.Output);
        Assert.Equal(long.MaxValue, operation.Responded);
    }

    [Fact]
    public async ValueTask RecordAsync_WhenCallNeverEnds_RecordsIndeterminateAfterTimeout()
    {
        var recorder = new HistoryRecorder(TimeSpan.FromMilliseconds(50));

        var operation = await recorder.RecordAsync<int>(
            0,
            Partition,
            "wait",
            async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            },
            Token
        );

        Assert.Equal(HistoryOutcome.Indeterminate, operation.Outcome);
        Assert.IsAssignableFrom<OperationCanceledException>(operation.Output);
        Assert.Equal(long.MaxValue, operation.Responded);
    }

    [Fact]
    public async ValueTask RecordAsync_WhenRunTokenIsCancelled_Rethrows()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var recorder = new HistoryRecorder();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await recorder.RecordAsync<int>(
                0,
                Partition,
                "wait",
                async token =>
                {
                    await source.CancelAsync();
                    token.ThrowIfCancellationRequested();
                    return 0;
                },
                source.Token
            )
        );

        var history = recorder.ToHistory();
        Assert.Empty(history);
    }

    [Fact]
    public async ValueTask RecordAsync_WhenCallThrowsAnotherException_Rethrows()
    {
        var recorder = new HistoryRecorder();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await recorder.RecordAsync<int>(
                0,
                Partition,
                "set",
                _ => ValueTask.FromException<int>(new InvalidOperationException()),
                Token
            )
        );

        var history = recorder.ToHistory();
        Assert.Empty(history);
    }

    [Fact]
    public async ValueTask RecordAsync_WhenCallFollowsAnotherCall_InvokesAfterItResponded()
    {
        var recorder = new HistoryRecorder();

        var first = await recorder.RecordAsync(
            0,
            Partition,
            "a",
            _ => ValueTask.FromResult(1),
            Token
        );
        var second = await recorder.RecordAsync(
            0,
            Partition,
            "b",
            _ => ValueTask.FromResult(2),
            Token
        );

        Assert.True(second.Invoked > first.Responded);
    }

    #endregion

    #region Stamp Tests

    [Fact]
    public void Stamp_WhenCalledTwice_ReturnsIncreasingStamps()
    {
        var recorder = new HistoryRecorder();

        var first = recorder.Stamp();
        var second = recorder.Stamp();

        Assert.True(second > first);
    }

    #endregion

    #region ToHistory Tests

    [Fact]
    public async ValueTask ToHistory_WhenCallsOverlap_OrdersByInvoked()
    {
        var recorder = new HistoryRecorder();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = recorder
            .RecordAsync(
                0,
                Partition,
                "slow",
                async _ =>
                {
                    await gate.Task;
                    return 1;
                },
                Token
            )
            .AsTask();

        await recorder.RecordAsync(1, Partition, "fast", _ => ValueTask.FromResult(2), Token);
        gate.SetResult();
        await slow;

        var inputs = recorder.ToHistory().Select(operation => operation.Input);
        Assert.Equal(["slow", "fast"], inputs);
    }

    #endregion
}
