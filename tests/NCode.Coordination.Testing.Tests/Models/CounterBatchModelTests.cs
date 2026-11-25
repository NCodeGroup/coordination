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

        var applied = model.TryApply(State(4, 5), operation, out var next);

        Assert.True(applied);
        Assert.Equal(State(4, 8), next);
    }

    [Fact]
    public void TryApply_WhenBatchStaysUnderTheLimit_AppliesBothIncrements()
    {
        var model = new CounterBatchModel();
        var result = new CounterBatchResult([5L, 9L]);
        var operation = HistoryTesting.Returned(Batch(2, 4, 10), result);

        var applied = model.TryApply(State(3, 5), operation, out var next);

        Assert.True(applied);
        Assert.Equal(State(5, 9), next);
    }

    [Fact]
    public void TryApply_WhenSecondCountExceedsTheLimitAndReportsIt_AppliesBothIncrements()
    {
        var model = new CounterBatchModel();
        var result = new CounterBatchResult([5L, 12L]) { ExceededIndex = 1 };
        var operation = HistoryTesting.Returned(Batch(2, 4, 10), result);

        var applied = model.TryApply(State(3, 8), operation, out var next);

        Assert.True(applied);
        Assert.Equal(State(5, 12), next);
    }

    [Fact]
    public void TryApply_WhenFirstCountExceedsTheLimit_StopsAndLeavesTheSecondKey()
    {
        var model = new CounterBatchModel();
        var result = new CounterBatchResult([12L]) { ExceededIndex = 0 };
        var operation = HistoryTesting.Returned(Batch(2, 4, 10), result);

        var applied = model.TryApply(State(10, 8), operation, out var next);

        Assert.True(applied);
        Assert.Equal(State(12, 8), next);
    }

    [Fact]
    public void TryApply_WhenBatchReturnsAWrongCount_Rejects()
    {
        var model = new CounterBatchModel();
        var result = new CounterBatchResult([5L, 10L]);
        var operation = HistoryTesting.Returned(Batch(2, 4, 10), result);

        var applied = model.TryApply(State(3, 5), operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchReturnsBothCountsAfterTheFirstExceedsTheLimit_Rejects()
    {
        var model = new CounterBatchModel();
        var result = new CounterBatchResult([12L, 12L]) { ExceededIndex = 1 };
        var operation = HistoryTesting.Returned(Batch(2, 4, 10), result);

        var applied = model.TryApply(State(10, 8), operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchOmitsTheExceededIndex_Rejects()
    {
        var model = new CounterBatchModel();
        var result = new CounterBatchResult([5L, 12L]);
        var operation = HistoryTesting.Returned(Batch(2, 4, 10), result);

        var applied = model.TryApply(State(3, 8), operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchIsIndeterminate_AppliesAsTheBatchWould()
    {
        var model = new CounterBatchModel();
        var operation = HistoryTesting.Indeterminate(Batch(2, 4, 10));

        var applied = model.TryApply(State(3, 5), operation, out var next);

        Assert.True(applied);
        Assert.Equal(State(5, 9), next);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = new CounterBatchModel();
        var operation = HistoryTesting.Returned("foreign", 1L);

        var applied = model.TryApply(State(0, 0), operation, out _);

        Assert.False(applied);
    }

    #endregion
}
