// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecordWriteTests
{
    private static CoordinationKey Key() => new(new Keyspace("prod"), "settings");

    #region Put Tests

    [Fact]
    public void Put_WhenArgumentsAreGiven_KeepsThem()
    {
        var key = Key();
        var value = "payload"u8.ToArray();
        var lifetime = RecordLifetime.Persistent;

        var write = RecordWrite.Put(key, value, lifetime);

        Assert.Equal(RecordWriteKind.Put, write.Kind);
        Assert.Same(key, write.Key);
        var matches = write.Value.Span.SequenceEqual(value);

        Assert.True(matches);
        Assert.Same(lifetime, write.Lifetime);
    }

    [Fact]
    public void Put_WhenValueHasLimitLength_Accepts()
    {
        var key = Key();

        var write = RecordWrite.Put(key, new byte[65_536], RecordLifetime.Persistent);

        Assert.Equal(65_536, write.Value.Length);
    }

    [Fact]
    public void Put_WhenValueIsOverLimit_Throws()
    {
        var key = Key();

        var exception = Assert.Throws<ArgumentException>(() =>
            RecordWrite.Put(key, new byte[65_537], RecordLifetime.Persistent)
        );

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Put_WhenSourceChangesAfterward_KeepsItsCopy()
    {
        var key = Key();
        var lifetime = RecordLifetime.Persistent;
        byte[] source = [1, 2, 3];

        var write = RecordWrite.Put(key, source, lifetime);

        source[0] = 9;
        var kept = write.Value.ToArray();

        Assert.Equal<byte>([1, 2, 3], kept);
    }

    [Fact]
    public void Put_WhenKeyIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordWrite.Put(null!, ReadOnlyMemory<byte>.Empty, RecordLifetime.Persistent)
        );

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Put_WhenLifetimeIsNull_Throws()
    {
        var key = Key();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordWrite.Put(key, ReadOnlyMemory<byte>.Empty, null!)
        );

        Assert.Equal("lifetime", exception.ParamName);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public void Delete_WhenKeyIsGiven_HasNoValueAndNoLifetime()
    {
        var key = Key();

        var write = RecordWrite.Delete(key);

        Assert.Equal(RecordWriteKind.Delete, write.Kind);
        Assert.Same(key, write.Key);
        Assert.True(write.Value.IsEmpty);
        Assert.Null(write.Lifetime);
    }

    [Fact]
    public void Delete_WhenKeyIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => RecordWrite.Delete(null!));

        Assert.Equal("key", exception.ParamName);
    }

    #endregion
}
