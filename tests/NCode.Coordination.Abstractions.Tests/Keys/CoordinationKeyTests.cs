// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class CoordinationKeyTests
{
    private static readonly Keyspace ProdKeyspace = new("prod", "cell-1");

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValues()
    {
        var key = new CoordinationKey(ProdKeyspace, "refresh", 42L);
        var refresh = KeySegment.FromString("refresh");
        var number = KeySegment.FromInt64(42);

        Assert.Same(ProdKeyspace, key.Keyspace);
        Assert.Equal<KeySegment>([refresh, number], key.Segments);
        Assert.Equal(0, key.PartitionDepth);
    }

    [Fact]
    public void Constructor_WhenKeyspaceIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CoordinationKey(null!, "a"));

        Assert.Equal("keyspace", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenSegmentsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CoordinationKey(ProdKeyspace, null!)
        );

        Assert.Equal("segments", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreNoSegments_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new CoordinationKey(ProdKeyspace));

        Assert.Equal("segments", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenASegmentIsNull_Throws()
    {
        KeySegment[] segments = [KeySegment.FromInt64(1), null!];

        var exception = Assert.Throws<ArgumentException>(() =>
            new CoordinationKey(ProdKeyspace, segments)
        );

        Assert.Equal("segments", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreMaxSegments_Succeeds()
    {
        var segments = Enumerable
            .Range(1, CoordinationKey.MaxSegments)
            .Select(x => KeySegment.FromInt64(x))
            .ToArray();

        var key = new CoordinationKey(ProdKeyspace, segments);

        Assert.Equal(CoordinationKey.MaxSegments, key.Segments.Length);
    }

    [Fact]
    public void Constructor_WhenThereAreTooManySegments_Throws()
    {
        var segments = Enumerable
            .Range(1, CoordinationKey.MaxSegments + 1)
            .Select(x => KeySegment.FromInt64(x))
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() =>
            new CoordinationKey(ProdKeyspace, segments)
        );

        Assert.Equal("segments", exception.ParamName);
    }

    #endregion

    #region PartitionDepth Property Tests

    [Fact]
    public void PartitionDepth_WhenEqualToTheSegmentCount_Succeeds()
    {
        var key = new CoordinationKey(ProdKeyspace, "a", "b") { PartitionDepth = 2 };

        Assert.Equal(2, key.PartitionDepth);
    }

    [Fact]
    public void PartitionDepth_WhenGreaterThanTheSegmentCount_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CoordinationKey(ProdKeyspace, "a", "b") { PartitionDepth = 3 }
        );

        Assert.Equal("PartitionDepth", exception.ParamName);
    }

    [Fact]
    public void PartitionDepth_WhenNegative_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CoordinationKey(ProdKeyspace, "a", "b") { PartitionDepth = -1 }
        );

        Assert.Equal("PartitionDepth", exception.ParamName);
    }

    [Fact]
    public void PartitionDepth_WhenSetWithAWithExpression_SetsIt()
    {
        var key = new CoordinationKey(ProdKeyspace, "a", "b");

        var partitioned = key with { PartitionDepth = 1 };

        Assert.Equal(1, partitioned.PartitionDepth);
        Assert.Equal(0, key.PartitionDepth);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenKeyspaceSegmentsAndDepthMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new CoordinationKey(new Keyspace("prod", "cell-1"), "a", 1L)
        {
            PartitionDepth = 1,
        };
        var second = new CoordinationKey(new Keyspace("prod", "cell-1"), "a", 1L)
        {
            PartitionDepth = 1,
        };

        var equal = first.Equals(second);
        var firstHash = first.GetHashCode();
        var secondHash = second.GetHashCode();

        Assert.True(equal);
        Assert.Equal(first, second);
        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public void Equals_WhenSameInstance_ReturnsTrue()
    {
        var key = new CoordinationKey(ProdKeyspace, "a");

        var equal = key.Equals(key);

        Assert.True(equal);
    }

    [Fact]
    public void Equals_WhenOtherIsNull_ReturnsFalse()
    {
        var key = new CoordinationKey(ProdKeyspace, "a");

        var equal = key.Equals(null);

        Assert.False(equal);
    }

    [Fact]
    public void Equals_WhenOnlyPartitionDepthDiffers_ReturnsFalse()
    {
        var first = new CoordinationKey(ProdKeyspace, "a", "b");
        var second = first with { PartitionDepth = 1 };

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_WhenKeyspaceDiffers_ReturnsFalse()
    {
        var first = new CoordinationKey(new Keyspace("prod"), "a");
        var second = new CoordinationKey(new Keyspace("test"), "a");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_WhenSegmentsDiffer_ReturnsFalse()
    {
        var first = new CoordinationKey(ProdKeyspace, "a", "b");
        var second = new CoordinationKey(ProdKeyspace, "b", "a");
        var third = new CoordinationKey(ProdKeyspace, "a");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, third);
    }

    #endregion

    #region IsPrefixOf Tests

    [Fact]
    public void IsPrefixOf_WhenLeadingSegmentsMatch_ReturnsTrue()
    {
        var prefix = new CoordinationKey(ProdKeyspace, "a");
        var key = new CoordinationKey(ProdKeyspace, "a", "b");

        var isPrefix = prefix.IsPrefixOf(key);

        Assert.True(isPrefix);
    }

    [Fact]
    public void IsPrefixOf_WhenOtherIsShorter_ReturnsFalse()
    {
        var prefix = new CoordinationKey(ProdKeyspace, "a");
        var key = new CoordinationKey(ProdKeyspace, "a", "b");

        var isPrefix = key.IsPrefixOf(prefix);

        Assert.False(isPrefix);
    }

    [Fact]
    public void IsPrefixOf_WhenOtherIsTheSameKey_ReturnsTrue()
    {
        var key = new CoordinationKey(ProdKeyspace, "a", "b");

        var isPrefix = key.IsPrefixOf(key);

        Assert.True(isPrefix);
    }

    [Fact]
    public void IsPrefixOf_WhenASegmentIsOnlyAPrefixOfText_ReturnsFalse()
    {
        var prefix = new CoordinationKey(ProdKeyspace, "ab");
        var key = new CoordinationKey(ProdKeyspace, "abc");

        var isPrefix = prefix.IsPrefixOf(key);

        Assert.False(isPrefix);
    }

    [Fact]
    public void IsPrefixOf_WhenKeyspaceDiffers_ReturnsFalse()
    {
        var prefix = new CoordinationKey(new Keyspace("prod"), "a");
        var key = new CoordinationKey(new Keyspace("test"), "a", "b");

        var isPrefix = prefix.IsPrefixOf(key);

        Assert.False(isPrefix);
    }

    [Fact]
    public void IsPrefixOf_WhenPartitionDepthDiffers_ReturnsTrue()
    {
        var prefix = new CoordinationKey(ProdKeyspace, "a");
        var key = new CoordinationKey(ProdKeyspace, "a", "b") { PartitionDepth = 2 };

        var isPrefix = prefix.IsPrefixOf(key);

        Assert.True(isPrefix);
    }

    [Fact]
    public void IsPrefixOf_WhenOtherIsNull_Throws()
    {
        var key = new CoordinationKey(ProdKeyspace, "a");

        Assert.Throws<ArgumentNullException>(() => key.IsPrefixOf(null!));
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_WhenThereAreSegments_JoinsThemWithSlashesAfterTheKeyspace()
    {
        var key = new CoordinationKey(ProdKeyspace, "refresh", 42L);

        var text = key.ToString();

        Assert.Equal("prod/cell-1:refresh/42", text);
    }

    [Fact]
    public void ToString_WhenThereIsOneSegment_HasNoSlash()
    {
        var key = new CoordinationKey(new Keyspace("prod"), "a");

        var text = key.ToString();

        Assert.Equal("prod:a", text);
    }

    #endregion
}
