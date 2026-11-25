// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class StoredRecordTests
{
    private static CoordinationKey Key() => new(new Keyspace("prod"), "settings");

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsAll()
    {
        var key = Key();
        var revision = new Revision(9);
        var value = "payload"u8.ToArray();

        var record = new StoredRecord(key, value, revision);

        Assert.Same(key, record.Key);
        Assert.Same(revision, record.Revision);
        Assert.Equal<byte>(value, record.Value.ToArray());
    }

    [Fact]
    public void Constructor_WhenValueIsEmpty_Accepts()
    {
        var record = new StoredRecord(Key(), ReadOnlyMemory<byte>.Empty, new Revision(9));

        Assert.True(record.Value.IsEmpty);
    }

    [Fact]
    public void Constructor_WhenValueIsAtTheLimit_Accepts()
    {
        var value = new byte[CoordinationLimits.MaxValueLength];

        var record = new StoredRecord(Key(), value, new Revision(9));

        Assert.Equal(65_536, record.Value.Length);
    }

    [Fact]
    public void Constructor_WhenValueIsAboveTheLimit_Throws()
    {
        var value = new byte[CoordinationLimits.MaxValueLength + 1];

        var exception = Assert.Throws<ArgumentException>(() =>
            new StoredRecord(Key(), value, new Revision(9))
        );

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenKeyIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StoredRecord(null!, ReadOnlyMemory<byte>.Empty, new Revision(9))
        );

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenRevisionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StoredRecord(Key(), ReadOnlyMemory<byte>.Empty, null!)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    #endregion
}
