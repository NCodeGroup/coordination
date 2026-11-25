// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecordPageTests
{
    private static StoredRecord Record(string name)
    {
        var key = new CoordinationKey(new Keyspace("prod"), name);
        var value = "v"u8.ToArray();

        return new StoredRecord(key, value, new Revision(3));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsAll()
    {
        var revision = new Revision(9);
        var first = Record("a");
        var second = Record("b");

        var page = new RecordPage(revision, first, second);

        Assert.Same(revision, page.Revision);
        Assert.Equal<StoredRecord>([first, second], page.Records);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public void Constructor_WhenThereAreNoRecords_Accepts()
    {
        var page = new RecordPage(new Revision(9));

        Assert.Empty(page.Records);
    }

    [Fact]
    public void Constructor_WhenRevisionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new RecordPage(null!));

        Assert.Equal("revision", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenRecordsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RecordPage(new Revision(9), (IEnumerable<StoredRecord>)null!)
        );

        Assert.Equal("records", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenARecordIsNull_Throws()
    {
        var first = Record("a");

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordPage(new Revision(9), first, null!)
        );

        Assert.Equal("records", exception.ParamName);
    }

    #endregion

    #region ContinuationToken Property Tests

    [Fact]
    public void ContinuationToken_WhenValid_KeepsIt()
    {
        var page = new RecordPage(new Revision(9)) { ContinuationToken = "next-1" };

        Assert.Equal("next-1", page.ContinuationToken);
    }

    [Fact]
    public void ContinuationToken_WhenNullIsSet_IsNull()
    {
        var page = new RecordPage(new Revision(9)) { ContinuationToken = null };

        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public void ContinuationToken_WhenInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordPage(new Revision(9)) { ContinuationToken = "a b" }
        );

        Assert.Equal("ContinuationToken", exception.ParamName);
    }

    #endregion
}
