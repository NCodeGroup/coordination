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

        Assert.Same(ProdKeyspace, key.Keyspace);
        Assert.Equal<KeySegment>(
            [KeySegment.FromString("refresh"), KeySegment.FromInt64(42)],
            key.Segments
        );
        Assert.Equal(0, key.PartitionDepth);
    }

    [Fact]
    public void Constructor_WhenKeyspaceIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CoordinationKey(null!, "a"));
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

    #region PartitionDepth Tests

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

        Assert.True(first.Equals(second));
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_WhenSameInstance_ReturnsTrue()
    {
        var key = new CoordinationKey(ProdKeyspace, "a");

        Assert.True(key.Equals(key));
    }

    [Fact]
    public void Equals_WhenOtherIsNull_ReturnsFalse()
    {
        var key = new CoordinationKey(ProdKeyspace, "a");

        Assert.False(key.Equals(null));
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

        Assert.True(prefix.IsPrefixOf(key));
    }

    [Fact]
    public void IsPrefixOf_WhenOtherIsShorter_ReturnsFalse()
    {
        var prefix = new CoordinationKey(ProdKeyspace, "a");
        var key = new CoordinationKey(ProdKeyspace, "a", "b");

        Assert.False(key.IsPrefixOf(prefix));
    }

    [Fact]
    public void IsPrefixOf_WhenOtherIsTheSameKey_ReturnsTrue()
    {
        var key = new CoordinationKey(ProdKeyspace, "a", "b");

        Assert.True(key.IsPrefixOf(key));
    }

    [Fact]
    public void IsPrefixOf_WhenASegmentIsOnlyAPrefixOfText_ReturnsFalse()
    {
        var prefix = new CoordinationKey(ProdKeyspace, "ab");
        var key = new CoordinationKey(ProdKeyspace, "abc");

        Assert.False(prefix.IsPrefixOf(key));
    }

    [Fact]
    public void IsPrefixOf_WhenKeyspaceDiffers_ReturnsFalse()
    {
        var prefix = new CoordinationKey(new Keyspace("prod"), "a");
        var key = new CoordinationKey(new Keyspace("test"), "a", "b");

        Assert.False(prefix.IsPrefixOf(key));
    }

    [Fact]
    public void IsPrefixOf_WhenPartitionDepthDiffers_ReturnsTrue()
    {
        var prefix = new CoordinationKey(ProdKeyspace, "a");
        var key = new CoordinationKey(ProdKeyspace, "a", "b") { PartitionDepth = 2 };

        Assert.True(prefix.IsPrefixOf(key));
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

        Assert.Equal("prod/cell-1:refresh/42", key.ToString());
    }

    [Fact]
    public void ToString_WhenThereIsOneSegment_HasNoSlash()
    {
        var key = new CoordinationKey(new Keyspace("prod"), "a");

        Assert.Equal("prod:a", key.ToString());
    }

    #endregion

    #region CreateKey Tests

    [Fact]
    public void CreateKey_WhenCalled_EqualsTheConstructedKey()
    {
        var created = ProdKeyspace.CreateKey("refresh", 42L);

        Assert.Equal(new CoordinationKey(ProdKeyspace, "refresh", 42L), created);
    }

    #endregion
}
