// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Checking.Tests;

public sealed class RecordWatchDeliveryCheckerTests
{
    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Name { get; } = new(Space, "record");

    private static IReadOnlyDictionary<CoordinationKey, Revision> NoClosing { get; } =
        ImmutableDictionary<CoordinationKey, Revision>.Empty;

    private static HistoryOperation Write(string operation, string value, long revision) =>
        HistoryTesting.Operation(
            0,
            Name.ToString(),
            new RecordCall(operation, value, 0),
            1,
            2,
            RecordWriteResult.Written(new Revision(revision))
        );

    private static HistoryOperation Delete(object? output) =>
        HistoryTesting.Operation(
            0,
            Name.ToString(),
            new RecordCall(RecordCall.CompareAndDelete, null, 1),
            1,
            2,
            output
        );

    private static HistoryOperation Take(object? output) =>
        HistoryTesting.Operation(
            0,
            Name.ToString(),
            new RecordCall(RecordCall.Take, null, 0),
            1,
            2,
            output
        );

    private static Delivery<RecordChange> Written(long stamp, string value, long revision) =>
        new(
            stamp,
            RecordChange.Written(
                new StoredRecord(Name, HistoryValues.Encode(value), new Revision(revision))
            )
        );

    private static Delivery<RecordChange> Deleted(long stamp, long revision) =>
        new(stamp, RecordChange.Deleted(Name, new Revision(revision)));

    private static Delivery<RecordChange> Resync(long stamp, long revision) =>
        new(stamp, RecordChange.Resync(new Revision(revision)));

    private static ImmutableArray<Delivery<RecordChange>> Changes(
        params Delivery<RecordChange>[] changes
    ) => [.. changes];

    private static IReadOnlyDictionary<CoordinationKey, Revision> Closing(long revision) =>
        ImmutableDictionary<CoordinationKey, Revision>.Empty.Add(Name, new Revision(revision));

    #region Check Tests

    [Fact]
    public void Check_WhenChangesAreClean_ReportsNothing()
    {
        HistoryOperation[] history =
        [
            Write(RecordCall.Create, "a", 1),
            Write(RecordCall.CompareAndSwap, "b", 2),
            Delete(RecordDeleteStatus.Deleted),
            Write(RecordCall.Create, "c", 4),
        ];
        var changes = Changes(
            Written(3, "a", 1),
            Written(4, "b", 2),
            Deleted(5, 3),
            Written(6, "c", 4)
        );

        var violations = RecordWatchDeliveryChecker.Check(history, changes, Closing(4), true);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenAResyncIsBelowTheFloor_ReportsIt()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 3)];
        var changes = Changes(Written(3, "a", 3), Resync(4, 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(["The watch resynced at revision 2 after revision 3."], violations);
    }

    [Fact]
    public void Check_WhenARevisionDoesNotIncrease_ReportsIt()
    {
        HistoryOperation[] history =
        [
            Write(RecordCall.Create, "a", 2),
            Write(RecordCall.CompareAndSwap, "b", 2),
        ];
        var changes = Changes(Written(3, "a", 2), Written(4, "b", 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(["The watch received revision 2 after revision 2."], violations);
    }

    [Fact]
    public void Check_WhenNoWriteProducedAValue_ReportsIt()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 1)];
        var changes = Changes(Written(3, "x", 1));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(
            [$"The watch received 'x' at revision 1 for {Name}, which no write produced."],
            violations
        );
    }

    [Fact]
    public void Check_WhenTheWriteWasAtAnotherRevision_ReportsIt()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 1)];
        var changes = Changes(Written(3, "a", 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(
            [$"The watch received 'a' at revision 2 for {Name}, which no write produced."],
            violations
        );
    }

    [Fact]
    public void Check_WhenTheWriteCameAfterTheChange_ReportsIt()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(
                0,
                Name.ToString(),
                new RecordCall(RecordCall.Create, "a", 0),
                5,
                6,
                RecordWriteResult.Written(new Revision(1))
            ),
        ];
        var changes = Changes(Written(3, "a", 1));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(
            [$"The watch received 'a' at revision 1 for {Name}, which no write produced."],
            violations
        );
    }

    [Fact]
    public void Check_WhenTheWriteBelongsToAnotherKey_ReportsIt()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(
                0,
                new CoordinationKey(Space, "other").ToString(),
                new RecordCall(RecordCall.Create, "a", 0),
                1,
                2,
                RecordWriteResult.Written(new Revision(1))
            ),
        ];
        var changes = Changes(Written(3, "a", 1));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Single(violations);
    }

    [Fact]
    public void Check_WhenTheWriteConflicted_ReportsIt()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Returned(
                new RecordCall(RecordCall.Create, "a", 0),
                RecordWriteResult.Conflict
            ) with
            {
                Partition = Name.ToString(),
            },
        ];
        var changes = Changes(Written(3, "a", 1));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Single(violations);
    }

    [Fact]
    public void Check_WhenTheWriteIsIndeterminate_AcceptsTheChange()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Indeterminate(new RecordCall(RecordCall.Create, "a", 0)) with
            {
                Partition = Name.ToString(),
            },
        ];
        var changes = Changes(Written(3, "a", 7));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenNoCallDeletedTheRecord_ReportsIt()
    {
        HistoryOperation[] history =
        [
            Write(RecordCall.Create, "a", 1),
            Delete(RecordDeleteStatus.Conflict),
            Take(null),
        ];
        var changes = Changes(Written(3, "a", 1), Deleted(4, 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(
            [$"The watch received a deletion of {Name} at revision 2, which no call made."],
            violations
        );
    }

    [Fact]
    public void Check_WhenATakeReturnedTheRecord_AcceptsTheDeletion()
    {
        HistoryOperation[] history =
        [
            Write(RecordCall.Create, "a", 1),
            Take(new StoredRecord(Name, HistoryValues.Encode("a"), new Revision(1))),
        ];
        var changes = Changes(Written(3, "a", 1), Deleted(4, 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenTheDeleteIsIndeterminate_AcceptsTheDeletion()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Indeterminate(new RecordCall(RecordCall.CompareAndDelete, null, 1)) with
            {
                Partition = Name.ToString(),
            },
        ];
        var changes = Changes(Deleted(3, 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenTheDeleteCameAfterTheChange_ReportsIt()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(
                0,
                Name.ToString(),
                new RecordCall(RecordCall.CompareAndDelete, null, 1),
                5,
                6,
                RecordDeleteStatus.Deleted
            ),
        ];
        var changes = Changes(Deleted(3, 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Single(violations);
    }

    [Fact]
    public void Check_WhenTheClosingWriteIsMissing_ReportsIt()
    {
        HistoryOperation[] history =
        [
            Write(RecordCall.Create, "a", 1),
            Write(RecordCall.CompareAndSwap, "b", 2),
        ];
        var changes = Changes(Written(3, "a", 1));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, Closing(2), true);

        Assert.Equal(
            [
                $"The watch received neither the last write to {Name}, at revision 2, nor a resync after it.",
            ],
            violations
        );
    }

    [Fact]
    public void Check_WhenAResyncCoversTheClosingWrite_ReportsNothing()
    {
        HistoryOperation[] history =
        [
            Write(RecordCall.Create, "a", 1),
            Write(RecordCall.CompareAndSwap, "b", 2),
        ];
        var changes = Changes(Written(3, "a", 1), Resync(4, 2));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, Closing(2), true);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenAResyncPrecedesTheClosingWrite_ReportsIt()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 3)];
        var changes = Changes(Resync(4, 1));

        var violations = RecordWatchDeliveryChecker.Check(history, changes, Closing(3), true);

        Assert.Single(violations);
    }

    [Fact]
    public void Check_WhenTheClosingWriteIsMissingAndGapDetectionIsNotDeclared_ReportsNothing()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 1)];
        var changes = Changes();

        var violations = RecordWatchDeliveryChecker.Check(history, changes, Closing(1), false);

        Assert.Empty(violations);
    }

    #endregion
}
