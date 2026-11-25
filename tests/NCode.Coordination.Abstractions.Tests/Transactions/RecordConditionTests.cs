// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecordConditionTests
{
    private static CoordinationKey Key() => new(new Keyspace("prod"), "settings");

    #region Absent Tests

    [Fact]
    public void Absent_WhenKeyIsGiven_KeepsKeyWithoutRevisionOrValue()
    {
        var key = Key();

        var condition = RecordCondition.Absent(key);

        Assert.Equal(RecordConditionKind.Absent, condition.Kind);
        Assert.Same(key, condition.Key);
        Assert.Null(condition.Revision);
        Assert.True(condition.Value.IsEmpty);
    }

    [Fact]
    public void Absent_WhenKeyIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => RecordCondition.Absent(null!));

        Assert.Equal("key", exception.ParamName);
    }

    #endregion

    #region RevisionEquals Tests

    [Fact]
    public void RevisionEquals_WhenArgumentsAreGiven_KeepsKeyAndRevision()
    {
        var key = Key();
        var revision = new Revision(4);

        var condition = RecordCondition.RevisionEquals(key, revision);

        Assert.Equal(RecordConditionKind.RevisionEquals, condition.Kind);
        Assert.Same(key, condition.Key);
        Assert.Same(revision, condition.Revision);
        Assert.True(condition.Value.IsEmpty);
    }

    [Fact]
    public void RevisionEquals_WhenKeyIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordCondition.RevisionEquals(null!, new Revision(4))
        );

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void RevisionEquals_WhenRevisionIsNull_Throws()
    {
        var key = Key();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordCondition.RevisionEquals(key, null!)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    #endregion

    #region ValueEquals Tests

    [Fact]
    public void ValueEquals_WhenArgumentsAreGiven_KeepsKeyAndValue()
    {
        var key = Key();
        var value = "payload"u8.ToArray();

        var condition = RecordCondition.ValueEquals(key, value);

        Assert.Equal(RecordConditionKind.ValueEquals, condition.Kind);
        Assert.Same(key, condition.Key);
        Assert.Null(condition.Revision);
        var matches = condition.Value.Span.SequenceEqual(value);

        Assert.True(matches);
    }

    [Fact]
    public void ValueEquals_WhenValueHasLimitLength_Accepts()
    {
        var key = Key();

        var condition = RecordCondition.ValueEquals(key, new byte[65_536]);

        Assert.Equal(65_536, condition.Value.Length);
    }

    [Fact]
    public void ValueEquals_WhenValueIsOverLimit_Throws()
    {
        var key = Key();

        var exception = Assert.Throws<ArgumentException>(() =>
            RecordCondition.ValueEquals(key, new byte[65_537])
        );

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ValueEquals_WhenSourceChangesAfterward_KeepsItsCopy()
    {
        var key = Key();
        byte[] source = [1, 2, 3];

        var condition = RecordCondition.ValueEquals(key, source);

        source[0] = 9;
        var kept = condition.Value.ToArray();

        Assert.Equal<byte>([1, 2, 3], kept);
    }

    [Fact]
    public void ValueEquals_WhenKeyIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordCondition.ValueEquals(null!, ReadOnlyMemory<byte>.Empty)
        );

        Assert.Equal("key", exception.ParamName);
    }

    #endregion
}
