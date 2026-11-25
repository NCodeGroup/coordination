// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class TransactionModelTests
{
    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Name { get; } = new(Space, "transaction");

    private static TransactionModel CreateModel() => new();

    private static TransactionCall ReadFirst { get; } =
        new(TransactionCall.Read, TransactionCall.FirstKey, null, null);

    private static TransactionCall ReadSecond { get; } =
        new(TransactionCall.Read, TransactionCall.SecondKey, null, null);

    private static TransactionCall ExecuteWhenAbsent { get; } =
        new(TransactionCall.Execute, null, null, "new");

    private static TransactionCall ExecuteWhenOld { get; } =
        new(TransactionCall.Execute, null, "old", "new");

    private static TransactionState PresentState { get; } = new("old", "old");

    private static StoredRecord Stored(string value) =>
        new(Name, HistoryValues.Encode(value), new Revision(1));

    private static TransactionResult Result(TransactionStatus status) =>
        new(status, new Revision(1));

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenExecuteSucceedsAndTheConditionHolds_WritesBothKeys()
    {
        var model = CreateModel();
        var result = Result(TransactionStatus.Succeeded);
        var operation = HistoryTesting.Returned(ExecuteWhenAbsent, result);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new TransactionState("new", "new"), next);
    }

    [Fact]
    public void TryApply_WhenExecuteSucceedsAndTheConditionDoesNotHold_Rejects()
    {
        var model = CreateModel();
        var result = Result(TransactionStatus.Succeeded);
        var operation = HistoryTesting.Returned(ExecuteWhenOld, result);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenExecuteFailsAndTheConditionDoesNotHold_KeepsTheState()
    {
        var model = CreateModel();
        var result = Result(TransactionStatus.Failed);
        var operation = HistoryTesting.Returned(ExecuteWhenAbsent, result);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenExecuteFailsAndTheConditionHolds_Rejects()
    {
        var model = CreateModel();
        var result = Result(TransactionStatus.Failed);
        var operation = HistoryTesting.Returned(ExecuteWhenAbsent, result);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenExecuteReplacesTheValueOfBothKeys_WritesBothKeys()
    {
        var model = CreateModel();
        var result = Result(TransactionStatus.Succeeded);
        var operation = HistoryTesting.Returned(ExecuteWhenOld, result);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new TransactionState("new", "new"), next);
    }

    [Fact]
    public void TryApply_WhenReadOfTheFirstKeyReturnsNullWhileAbsent_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(ReadFirst, null);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenReadOfTheFirstKeyReturnsTheValue_KeepsTheState()
    {
        var model = CreateModel();
        var record = Stored("old");
        var operation = HistoryTesting.Returned(ReadFirst, record);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenReadOfTheSecondKeyReturnsNullWhileAbsent_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(ReadSecond, null);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenReadOfTheSecondKeyReturnsTheValue_KeepsTheState()
    {
        var model = CreateModel();
        var record = Stored("old");
        var operation = HistoryTesting.Returned(ReadSecond, record);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenReadReturnsAWrongValue_Rejects()
    {
        var model = CreateModel();
        var record = Stored("other");
        var operation = HistoryTesting.Returned(ReadFirst, record);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsNullWhilePresent_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(ReadFirst, null);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadOfTheSecondKeyReturnsNullAfterTheFirstKeyWasWritten_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(ReadSecond, null);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadOfTheSecondKeyReturnsAValueWhileTheFirstKeyIsAbsent_Rejects()
    {
        var model = CreateModel();
        var record = Stored("new");
        var operation = HistoryTesting.Returned(ReadSecond, record);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenExecuteIsIndeterminateAndTheConditionHolds_WritesBothKeys()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(ExecuteWhenAbsent);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new TransactionState("new", "new"), next);
    }

    [Fact]
    public void TryApply_WhenExecuteIsIndeterminateAndTheConditionDoesNotHold_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(ExecuteWhenAbsent);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenReadIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(ReadFirst);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenExecuteReturnsSessionEnded_Rejects()
    {
        var model = CreateModel();
        var result = Result(TransactionStatus.SessionEnded);
        var operation = HistoryTesting.Returned(ExecuteWhenAbsent, result);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadKeyIsUnknown_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            new TransactionCall(TransactionCall.Read, "c", null, null),
            null
        );

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            new TransactionCall("other", null, null, null),
            null
        );

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var result = Result(TransactionStatus.Succeeded);
        var operation = HistoryTesting.Returned("execute", result);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenExecuteReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(ExecuteWhenAbsent, 1L);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
