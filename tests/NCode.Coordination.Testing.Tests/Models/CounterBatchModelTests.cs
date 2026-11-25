// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class CounterBatchModelTests
{
    private static CounterBatchCall Batch(long first, long second, long limit) =>
        new(first, second, limit);

    private static PairState<long> State(long first, long second) => new(first, second);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenKeyedIncrementReturnsTheSum_AppliesToItsKeyOnly()
    {
        var model = new CounterBatchModel();
        var call = new KeyedCall(1, new CounterCall(CounterCall.Increment, 3, 0));
        var operation = HistoryTesting.Returned(call, 8L);
        var state = State(4, 5);

        var applied = model.TryApply(state, operation, out var next);

        var expected = State(4, 8);
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenBatchStaysUnderTheLimit_AppliesBothIncrements()
    {
        var model = new CounterBatchModel();
        var call = Batch(2, 4, 10);
        var result = new CounterBatchResult([5L, 9L]);
        var operation = HistoryTesting.Returned(call, result);
        var state = State(3, 5);

        var applied = model.TryApply(state, operation, out var next);

        var expected = State(5, 9);
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenSecondCountExceedsTheLimitAndReportsIt_AppliesBothIncrements()
    {
        var model = new CounterBatchModel();
        var call = Batch(2, 4, 10);
        var result = new CounterBatchResult([5L, 12L]) { ExceededIndex = 1 };
        var operation = HistoryTesting.Returned(call, result);
        var state = State(3, 8);

        var applied = model.TryApply(state, operation, out var next);

        var expected = State(5, 12);
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenFirstCountExceedsTheLimit_StopsAndLeavesTheSecondKey()
    {
        var model = new CounterBatchModel();
        var call = Batch(2, 4, 10);
        var result = new CounterBatchResult([12L]) { ExceededIndex = 0 };
        var operation = HistoryTesting.Returned(call, result);
        var state = State(10, 8);

        var applied = model.TryApply(state, operation, out var next);

        var expected = State(12, 8);
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenBatchReturnsAWrongCount_Rejects()
    {
        var model = new CounterBatchModel();
        var call = Batch(2, 4, 10);
        var result = new CounterBatchResult([5L, 10L]);
        var operation = HistoryTesting.Returned(call, result);
        var state = State(3, 5);

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchReturnsBothCountsAfterTheFirstExceedsTheLimit_Rejects()
    {
        var model = new CounterBatchModel();
        var call = Batch(2, 4, 10);
        var result = new CounterBatchResult([12L, 12L]) { ExceededIndex = 1 };
        var operation = HistoryTesting.Returned(call, result);
        var state = State(10, 8);

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchOmitsTheExceededIndex_Rejects()
    {
        var model = new CounterBatchModel();
        var call = Batch(2, 4, 10);
        var result = new CounterBatchResult([5L, 12L]);
        var operation = HistoryTesting.Returned(call, result);
        var state = State(3, 8);

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchIsIndeterminate_AppliesAsTheBatchWould()
    {
        var model = new CounterBatchModel();
        var call = Batch(2, 4, 10);
        var operation = HistoryTesting.Indeterminate(call);
        var state = State(3, 5);

        var applied = model.TryApply(state, operation, out var next);

        var expected = State(5, 9);
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = new CounterBatchModel();
        var operation = HistoryTesting.Returned("foreign", 1L);
        var state = State(0, 0);

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
