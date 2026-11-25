// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class SequenceModelTests
{
    private static SequenceCall Next { get; } = new(SequenceCall.Next, 1);

    private static SequenceCall Reserve(int count) => new(SequenceCall.Reserve, count);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenNextReturnsTheFollowingNumber_AppliesIt()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned(Next, 6L);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(6, next);
    }

    [Fact]
    public void TryApply_WhenNextSkipsANumber_Rejects()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned(Next, 7L);

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReserveReturnsTheFollowingBlock_AppliesIt()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned(Reserve(3), new SequenceBlock(6, 3));

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(8, next);
    }

    [Fact]
    public void TryApply_WhenReserveReturnsABlockStartingElsewhere_Rejects()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned(Reserve(3), new SequenceBlock(7, 3));

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReserveReturnsABlockOfAnotherSize_Rejects()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned(Reserve(3), new SequenceBlock(6, 2));

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReserveReturnsANumber_Rejects()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned(Reserve(3), 6L);

        var applied = model.TryApply(5, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenNextIsIndeterminate_AppliesOneNumber()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Indeterminate(Next);

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(6, next);
    }

    [Fact]
    public void TryApply_WhenReserveIsIndeterminate_AppliesTheWholeBlock()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Indeterminate(Reserve(3));

        var applied = model.TryApply(5, operation, out var next);

        Assert.True(applied);
        Assert.Equal(8, next);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned("next", 1L);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = new SequenceModel();
        var operation = HistoryTesting.Returned(new SequenceCall("skip", 1), 1L);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
