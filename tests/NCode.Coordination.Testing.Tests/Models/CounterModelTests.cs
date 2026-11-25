// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class CounterModelTests
{
    private static CounterCall Increment(long amount) => new(CounterCall.Increment, amount, 0);

    private static CounterCall Batch(long amount, long limit) =>
        new(CounterCall.Batch, amount, limit);

    private static CounterCall Read { get; } = new(CounterCall.Read, 0, 0);

    private static CounterCall Reset { get; } = new(CounterCall.Reset, 0, 0);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenIncrementReturnsTheSum_AppliesIt()
    {
        var model = new CounterModel();
        var call = Increment(3);
        var operation = HistoryTesting.Returned(call, 8L);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(8, next);
    }

    [Fact]
    public void TryApply_WhenIncrementReturnsAWrongCount_Rejects()
    {
        var model = new CounterModel();
        var call = Increment(3);
        var operation = HistoryTesting.Returned(call, 7L);

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchReturnsTheSum_AppliesIt()
    {
        var model = new CounterModel();
        var call = Batch(2, 10);
        var result = new CounterBatchResult([7L]);
        var operation = HistoryTesting.Returned(call, result);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(7, next);
    }

    [Fact]
    public void TryApply_WhenBatchExceedsItsLimitAndReportsIt_AppliesIt()
    {
        var model = new CounterModel();
        var call = Batch(2, 10);
        var result = new CounterBatchResult([12L]) { ExceededIndex = 0 };
        var operation = HistoryTesting.Returned(call, result);

        var applied = model.TryApply(10, operation, out var next);

        Assert.True(applied);
        Assert.Equal(12, next);
    }

    [Fact]
    public void TryApply_WhenBatchReturnsAWrongCount_Rejects()
    {
        var model = new CounterModel();
        var call = Batch(2, 10);
        var result = new CounterBatchResult([8L]);
        var operation = HistoryTesting.Returned(call, result);

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchExceedsItsLimitWithoutExceededIndex_Rejects()
    {
        var model = new CounterModel();
        var call = Batch(2, 10);
        var result = new CounterBatchResult([12L]);
        var operation = HistoryTesting.Returned(call, result);

        var applied = model.TryApply(10, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchReportsAnExceededIndexWithinItsLimit_Rejects()
    {
        var model = new CounterModel();
        var call = Batch(2, 10);
        var result = new CounterBatchResult([7L]) { ExceededIndex = 0 };
        var operation = HistoryTesting.Returned(call, result);

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenBatchReturnsANumber_Rejects()
    {
        var model = new CounterModel();
        var call = Batch(2, 10);
        var operation = HistoryTesting.Returned(call, 7L);

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsTheCount_KeepsTheState()
    {
        var model = new CounterModel();
        var operation = HistoryTesting.Returned(Read, 5L);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(5, next);
    }

    [Fact]
    public void TryApply_WhenReadReturnsAWrongCount_Rejects()
    {
        var model = new CounterModel();
        var operation = HistoryTesting.Returned(Read, 4L);

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenResetReturnsAnything_SetsTheStateToZero()
    {
        var model = new CounterModel();
        var operation = HistoryTesting.Returned(Reset, null);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0, next);
    }

    [Fact]
    public void TryApply_WhenIncrementIsIndeterminate_AppliesTheSum()
    {
        var model = new CounterModel();
        var call = Increment(3);
        var operation = HistoryTesting.Indeterminate(call);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(8, next);
    }

    [Fact]
    public void TryApply_WhenBatchIsIndeterminate_AppliesTheSum()
    {
        var model = new CounterModel();
        var call = Batch(2, 10);
        var operation = HistoryTesting.Indeterminate(call);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(7, next);
    }

    [Fact]
    public void TryApply_WhenReadIsIndeterminate_KeepsTheState()
    {
        var model = new CounterModel();
        var operation = HistoryTesting.Indeterminate(Read);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(5, next);
    }

    [Fact]
    public void TryApply_WhenResetIsIndeterminate_SetsTheStateToZero()
    {
        var model = new CounterModel();
        var operation = HistoryTesting.Indeterminate(Reset);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0, next);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = new CounterModel();
        var operation = HistoryTesting.Returned("increment", 1L);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = new CounterModel();
        var operation = HistoryTesting.Returned(new CounterCall("decrement", 1, 0), 1L);

        var applied = model.TryApply(2, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
