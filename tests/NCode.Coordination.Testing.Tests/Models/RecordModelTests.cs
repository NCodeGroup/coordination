// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class RecordModelTests
{
    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Name { get; } = new(Space, "record");

    private static RecordModel CreateModel() => new();

    private static RecordCall Create { get; } = new(RecordCall.Create, "new", 0);

    private static RecordCall Read { get; } = new(RecordCall.Read, null, 0);

    private static RecordCall Swap { get; } = new(RecordCall.CompareAndSwap, "next", 5);

    private static RecordCall Delete { get; } = new(RecordCall.CompareAndDelete, null, 5);

    private static RecordCall Take { get; } = new(RecordCall.Take, null, 0);

    private static RecordState PresentState { get; } = new("old", 5, 5);

    private static StoredRecord Stored(string value, long revision)
    {
        var encoded = HistoryValues.Encode(value);
        return new(Name, encoded, new Revision(revision));
    }

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenCreateIsWrittenWhileAbsent_StoresTheRecord()
    {
        var model = CreateModel();
        var result = RecordWriteResult.Written(new Revision(3));
        var operation = HistoryTesting.Returned(Create, result);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState("new", 3, 3), next);
    }

    [Fact]
    public void TryApply_WhenCreateRevisionIsNotAboveTheFloor_Rejects()
    {
        var model = CreateModel();
        var result = RecordWriteResult.Written(new Revision(3));
        var operation = HistoryTesting.Returned(Create, result);
        var state = model.Initial with { Floor = 3 };

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenCreateConflictsWhilePresent_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Create, RecordWriteResult.Conflict);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenCreateConflictsWhileAbsent_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Create, RecordWriteResult.Conflict);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenCreateIsWrittenWhilePresent_Rejects()
    {
        var model = CreateModel();
        var result = RecordWriteResult.Written(new Revision(6));
        var operation = HistoryTesting.Returned(Create, result);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsNullWhileAbsent_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, null);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenReadReturnsNullWhilePresent_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, null);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsTheRecordWhilePresent_KeepsTheState()
    {
        var model = CreateModel();
        var record = Stored("old", 5);
        var operation = HistoryTesting.Returned(Read, record);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenReadReturnsARecordWhileAbsent_Rejects()
    {
        var model = CreateModel();
        var record = Stored("old", 5);
        var operation = HistoryTesting.Returned(Read, record);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsAWrongValue_Rejects()
    {
        var model = CreateModel();
        var record = Stored("other", 5);
        var operation = HistoryTesting.Returned(Read, record);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsAWrongRevision_Rejects()
    {
        var model = CreateModel();
        var record = Stored("old", 6);
        var operation = HistoryTesting.Returned(Read, record);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenSwapIsWrittenAtTheExpectedRevision_StoresTheValue()
    {
        var model = CreateModel();
        var result = RecordWriteResult.Written(new Revision(6));
        var operation = HistoryTesting.Returned(Swap, result);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState("next", 6, 6), next);
    }

    [Fact]
    public void TryApply_WhenSwapIsWrittenAtAnotherRevision_Rejects()
    {
        var model = CreateModel();
        var result = RecordWriteResult.Written(new Revision(6));
        var operation = HistoryTesting.Returned(Swap with { Expected = 4 }, result);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenSwapConflictsAtAnotherRevision_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            Swap with
            {
                Expected = 4,
            },
            RecordWriteResult.Conflict
        );

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenSwapConflictsAtTheExpectedRevision_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Swap, RecordWriteResult.Conflict);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenSwapIsNotFoundWhileAbsent_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Swap, RecordWriteResult.NotFound);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenSwapIsNotFoundWhilePresent_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Swap, RecordWriteResult.NotFound);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenDeleteIsDeletedAtTheExpectedRevision_RemovesTheRecord()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Delete, RecordDeleteStatus.Deleted);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState(null, 0, 5), next);
    }

    [Fact]
    public void TryApply_WhenDeleteIsDeletedAtAnotherRevision_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            Delete with
            {
                Expected = 4,
            },
            RecordDeleteStatus.Deleted
        );

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenDeleteConflictsAtAnotherRevision_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            Delete with
            {
                Expected = 4,
            },
            RecordDeleteStatus.Conflict
        );

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenDeleteIsNotFoundWhileAbsent_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Delete, RecordDeleteStatus.NotFound);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenTakeReturnsTheRecordWhilePresent_RemovesTheRecord()
    {
        var model = CreateModel();
        var record = Stored("old", 5);
        var operation = HistoryTesting.Returned(Take, record);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState(null, 0, 5), next);
    }

    [Fact]
    public void TryApply_WhenTakeReturnsNullWhileAbsent_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Take, null);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenTakeReturnsNullWhilePresent_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Take, null);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenTakeReturnsAWrongValue_Rejects()
    {
        var model = CreateModel();
        var record = Stored("other", 5);
        var operation = HistoryTesting.Returned(Take, record);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenCreateIsIndeterminateWhileAbsent_StoresAnUnknownRevision()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Create);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState("new", 0, 0), next);
    }

    [Fact]
    public void TryApply_WhenReadShowsTheRevisionOfAnIndeterminateCreate_LearnsIt()
    {
        var model = CreateModel();
        var create = HistoryTesting.Indeterminate(Create);
        var record = Stored("new", 7);
        var read = HistoryTesting.Returned(Read, record);

        model.TryApply(model.Initial, create, out var unknown);
        Assert.NotNull(unknown);
        var applied = model.TryApply(unknown, read, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState("new", 7, 7), next);
    }

    [Fact]
    public void TryApply_WhenReadShowsARevisionNotAboveTheFloorAfterAnIndeterminateCreate_Rejects()
    {
        var model = CreateModel();
        var record = Stored("new", 7);
        var read = HistoryTesting.Returned(Read, record);
        var state = new RecordState("new", 0, 7);

        var applied = model.TryApply(state, read, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenSwapIsIndeterminateAndMayMatch_StoresAnUnknownRevision()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Swap);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState("next", 0, 5), next);
    }

    [Fact]
    public void TryApply_WhenSwapIsIndeterminateAtAnotherRevision_KeepsTheState()
    {
        var model = CreateModel();
        var call = Swap with { Expected = 4 };
        var operation = HistoryTesting.Indeterminate(call);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenSwapConflictsAfterAnIndeterminateSwap_KeepsTheState()
    {
        var model = CreateModel();
        var indeterminate = HistoryTesting.Indeterminate(Swap);
        var conflict = HistoryTesting.Returned(Swap, RecordWriteResult.Conflict);

        model.TryApply(PresentState, indeterminate, out var unknown);
        Assert.NotNull(unknown);
        var applied = model.TryApply(unknown, conflict, out var next);

        Assert.True(applied);
        Assert.Equal(unknown, next);
    }

    [Fact]
    public void TryApply_WhenDeleteIsIndeterminateAndMayMatch_RemovesTheRecord()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Delete);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState(null, 0, 5), next);
    }

    [Fact]
    public void TryApply_WhenTakeIsIndeterminate_RemovesTheRecord()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Take);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new RecordState(null, 0, 5), next);
    }

    [Fact]
    public void TryApply_WhenReadIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Read);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(new RecordCall("other", null, 0), null);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var result = RecordWriteResult.Written(new Revision(3));
        var operation = HistoryTesting.Returned("create", result);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenCreateReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Create, 1L);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
