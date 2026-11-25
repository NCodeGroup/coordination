// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class RecordListingModelTests
{
    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey FirstKey { get; } = Space.CreateKey("run", "0");

    private static CoordinationKey SecondKey { get; } = Space.CreateKey("run", "1");

    private static CoordinationKey OtherKey { get; } = Space.CreateKey("run", "2");

    private static RecordListingModel CreateModel() => new([FirstKey, SecondKey]);

    private static RecordCall Create { get; } = new(RecordCall.Create, "new", 0);

    private static RecordCall List { get; } = new(RecordCall.List, null, 0);

    private static PairState<RecordState> PresentState { get; } =
        new(new RecordState("one", 5, 5), new RecordState("two", 6, 6));

    private static StoredRecord Stored(CoordinationKey key, string value, long revision) =>
        new(key, HistoryValues.Encode(value), new Revision(revision));

    private static RecordPage Page(long revision, params StoredRecord[] records) =>
        new(new Revision(revision), records);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenKeyedCreateIsWritten_AppliesToItsKeyOnly()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            new KeyedCall(1, Create),
            RecordWriteResult.Written(new Revision(3))
        );

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(
            new PairState<RecordState>(model.Initial.First, new RecordState("new", 3, 3)),
            next
        );
    }

    [Fact]
    public void TryApply_WhenKeyedCallNamesAThirdKey_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            new KeyedCall(2, Create),
            RecordWriteResult.Written(new Revision(3))
        );

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLastPageOfEmptyPairIsEmpty_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(List, Page(1));

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenLastPageShowsBothPresentRecords_KeepsTheState()
    {
        var model = CreateModel();
        var page = Page(6, Stored(FirstKey, "one", 5), Stored(SecondKey, "two", 6));
        var operation = HistoryTesting.Returned(List, page);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenPageShowsTheRevisionOfAnIndeterminateCreate_LearnsIt()
    {
        var model = CreateModel();
        var state = model.Initial with { First = new RecordState("new", 0, 0) };
        var page = Page(4, Stored(FirstKey, "new", 4));
        var operation = HistoryTesting.Returned(List, page);

        var applied = model.TryApply(state, operation, out var next);

        Assert.True(applied);
        Assert.Equal(
            new PairState<RecordState>(new RecordState("new", 4, 4), model.Initial.Second),
            next
        );
    }

    [Fact]
    public void TryApply_WhenLastPageMissesAPresentRecord_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(List, Page(6, Stored(FirstKey, "one", 5)));

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenPageWithContinuationTokenMissesAPresentRecord_KeepsTheState()
    {
        var model = CreateModel();
        var page = new RecordPage(new Revision(6), Stored(FirstKey, "one", 5))
        {
            ContinuationToken = "more",
        };
        var operation = HistoryTesting.Returned(List, page);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenPageShowsAWrongValue_Rejects()
    {
        var model = CreateModel();
        var page = Page(6, Stored(FirstKey, "other", 5), Stored(SecondKey, "two", 6));
        var operation = HistoryTesting.Returned(List, page);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenPageListsAKeyOutsideTheRun_Rejects()
    {
        var model = CreateModel();
        var page = Page(6, Stored(OtherKey, "one", 5));
        var operation = HistoryTesting.Returned(List, page);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenPageListsAKeyTwice_Rejects()
    {
        var model = CreateModel();
        var page = Page(6, Stored(FirstKey, "one", 5), Stored(FirstKey, "one", 5));
        var operation = HistoryTesting.Returned(List, page);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenPageRevisionIsBelowAKeyFloor_Rejects()
    {
        var model = CreateModel();
        var page = new RecordPage(new Revision(5)) { ContinuationToken = "more" };
        var operation = HistoryTesting.Returned(List, page);

        var applied = model.TryApply(PresentState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRecordRevisionIsAboveThePageRevision_Rejects()
    {
        var model = CreateModel();
        var page = Page(4, Stored(FirstKey, "one", 5));
        var operation = HistoryTesting.Returned(List, page);

        var state = model.Initial with { First = new RecordState("one", 0, 0) };

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenListingIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(List);

        var applied = model.TryApply(PresentState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(PresentState, next);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned("foreign", null);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
