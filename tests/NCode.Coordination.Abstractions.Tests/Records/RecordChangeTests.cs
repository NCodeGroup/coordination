// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecordChangeTests
{
    private static CoordinationKey Key() => new(new Keyspace("prod"), "settings");

    #region Written Tests

    [Fact]
    public void Written_WhenRecordIsGiven_TakesKeyAndRevisionFromIt()
    {
        var key = Key();
        var record = new StoredRecord(key, "payload"u8.ToArray(), new Revision(9));

        var change = RecordChange.Written(record);

        Assert.Equal(RecordChangeKind.Written, change.Kind);
        Assert.Same(record.Key, change.Key);
        Assert.Same(record, change.Record);
        Assert.Same(record.Revision, change.Revision);
    }

    [Fact]
    public void Written_WhenRecordIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => RecordChange.Written(null!));

        Assert.Equal("record", exception.ParamName);
    }

    #endregion

    #region Deleted Tests

    [Fact]
    public void Deleted_WhenArgumentsAreGiven_KeepsKeyAndRevisionWithoutRecord()
    {
        var key = Key();
        var revision = new Revision(10);

        var change = RecordChange.Deleted(key, revision);

        Assert.Equal(RecordChangeKind.Deleted, change.Kind);
        Assert.Same(key, change.Key);
        Assert.Null(change.Record);
        Assert.Same(revision, change.Revision);
    }

    [Fact]
    public void Deleted_WhenKeyIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordChange.Deleted(null!, new Revision(10))
        );

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Deleted_WhenRevisionIsNull_Throws()
    {
        var key = Key();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordChange.Deleted(key, null!)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    #endregion

    #region Resync Tests

    [Fact]
    public void Resync_WhenRevisionIsGiven_HasNoKeyAndNoRecord()
    {
        var revision = new Revision(11);

        var change = RecordChange.Resync(revision);

        Assert.Equal(RecordChangeKind.Resync, change.Kind);
        Assert.Null(change.Key);
        Assert.Null(change.Record);
        Assert.Same(revision, change.Revision);
    }

    [Fact]
    public void Resync_WhenRevisionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => RecordChange.Resync(null!));

        Assert.Equal("revision", exception.ParamName);
    }

    #endregion

    #region IsResync Property Tests

    [Fact]
    public void IsResync_WhenResync_ReturnsTrue()
    {
        var change = RecordChange.Resync(new Revision(11));

        Assert.True(change.IsResync);
    }

    [Fact]
    public void IsResync_WhenWritten_ReturnsFalse()
    {
        var key = Key();
        var record = new StoredRecord(key, ReadOnlyMemory<byte>.Empty, new Revision(9));
        var change = RecordChange.Written(record);

        Assert.False(change.IsResync);
    }

    [Fact]
    public void IsResync_WhenDeleted_ReturnsFalse()
    {
        var key = Key();
        var change = RecordChange.Deleted(key, new Revision(10));

        Assert.False(change.IsResync);
    }

    #endregion
}
