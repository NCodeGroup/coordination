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

    private static HistoryOperation Write(string operation, string value, long revision)
    {
        var partition = Name.ToString();
        var result = RecordWriteResult.Written(new Revision(revision));
        return HistoryTesting.Operation(
            0,
            partition,
            new RecordCall(operation, value, 0),
            1,
            2,
            result
        );
    }

    private static HistoryOperation Delete(object? output)
    {
        var partition = Name.ToString();
        return HistoryTesting.Operation(
            0,
            partition,
            new RecordCall(RecordCall.CompareAndDelete, null, 1),
            1,
            2,
            output
        );
    }

    private static HistoryOperation Take(object? output)
    {
        var partition = Name.ToString();
        return HistoryTesting.Operation(
            0,
            partition,
            new RecordCall(RecordCall.Take, null, 0),
            1,
            2,
            output
        );
    }

    private static Delivery<RecordChange> Written(long stamp, string value, long revision)
    {
        var encoded = HistoryValues.Encode(value);
        var record = new StoredRecord(Name, encoded, new Revision(revision));
        var change = RecordChange.Written(record);
        return new(stamp, change);
    }

    private static Delivery<RecordChange> Deleted(long stamp, long revision)
    {
        var change = RecordChange.Deleted(Name, new Revision(revision));
        return new(stamp, change);
    }

    private static Delivery<RecordChange> Resync(long stamp, long revision)
    {
        var change = RecordChange.Resync(new Revision(revision));
        return new(stamp, change);
    }

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
        var created = Written(3, "a", 1);
        var swapped = Written(4, "b", 2);
        var deleted = Deleted(5, 3);
        var recreated = Written(6, "c", 4);
        var changes = Changes(created, swapped, deleted, recreated);
        var closing = Closing(4);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, closing, true);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenAResyncIsBelowTheFloor_ReportsIt()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 3)];
        var written = Written(3, "a", 3);
        var resync = Resync(4, 2);
        var changes = Changes(written, resync);

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
        var first = Written(3, "a", 2);
        var second = Written(4, "b", 2);
        var changes = Changes(first, second);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(["The watch received revision 2 after revision 2."], violations);
    }

    [Fact]
    public void Check_WhenNoWriteProducedAValue_ReportsIt()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 1)];
        var written = Written(3, "x", 1);
        var changes = Changes(written);

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
        var written = Written(3, "a", 2);
        var changes = Changes(written);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(
            [$"The watch received 'a' at revision 2 for {Name}, which no write produced."],
            violations
        );
    }

    [Fact]
    public void Check_WhenTheWriteCameAfterTheChange_ReportsIt()
    {
        var partition = Name.ToString();
        var result = RecordWriteResult.Written(new Revision(1));
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(
                0,
                partition,
                new RecordCall(RecordCall.Create, "a", 0),
                5,
                6,
                result
            ),
        ];
        var written = Written(3, "a", 1);
        var changes = Changes(written);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(
            [$"The watch received 'a' at revision 1 for {Name}, which no write produced."],
            violations
        );
    }

    [Fact]
    public void Check_WhenTheWriteBelongsToAnotherKey_ReportsIt()
    {
        var other = new CoordinationKey(Space, "other");
        var partition = other.ToString();
        var result = RecordWriteResult.Written(new Revision(1));
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(
                0,
                partition,
                new RecordCall(RecordCall.Create, "a", 0),
                1,
                2,
                result
            ),
        ];
        var written = Written(3, "a", 1);
        var changes = Changes(written);

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
        var written = Written(3, "a", 1);
        var changes = Changes(written);

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
        var written = Written(3, "a", 7);
        var changes = Changes(written);

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
        var written = Written(3, "a", 1);
        var deleted = Deleted(4, 2);
        var changes = Changes(written, deleted);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Equal(
            [$"The watch received a deletion of {Name} at revision 2, which no call made."],
            violations
        );
    }

    [Fact]
    public void Check_WhenATakeReturnedTheRecord_AcceptsTheDeletion()
    {
        var value = HistoryValues.Encode("a");
        var record = new StoredRecord(Name, value, new Revision(1));
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 1), Take(record)];
        var written = Written(3, "a", 1);
        var deleted = Deleted(4, 2);
        var changes = Changes(written, deleted);

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
        var deleted = Deleted(3, 2);
        var changes = Changes(deleted);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, NoClosing, false);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenTheDeleteCameAfterTheChange_ReportsIt()
    {
        var partition = Name.ToString();
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(
                0,
                partition,
                new RecordCall(RecordCall.CompareAndDelete, null, 1),
                5,
                6,
                RecordDeleteStatus.Deleted
            ),
        ];
        var deleted = Deleted(3, 2);
        var changes = Changes(deleted);

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
        var written = Written(3, "a", 1);
        var changes = Changes(written);
        var closing = Closing(2);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, closing, true);

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
        var written = Written(3, "a", 1);
        var resync = Resync(4, 2);
        var changes = Changes(written, resync);
        var closing = Closing(2);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, closing, true);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_WhenAResyncPrecedesTheClosingWrite_ReportsIt()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 3)];
        var resync = Resync(4, 1);
        var changes = Changes(resync);
        var closing = Closing(3);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, closing, true);

        Assert.Single(violations);
    }

    [Fact]
    public void Check_WhenTheClosingWriteIsMissingAndGapDetectionIsNotDeclared_ReportsNothing()
    {
        HistoryOperation[] history = [Write(RecordCall.Create, "a", 1)];
        var changes = Changes();
        var closing = Closing(1);

        var violations = RecordWatchDeliveryChecker.Check(history, changes, closing, false);

        Assert.Empty(violations);
    }

    #endregion
}
