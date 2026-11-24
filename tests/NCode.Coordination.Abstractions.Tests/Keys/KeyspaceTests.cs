// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class KeyspaceTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValues()
    {
        const string cluster = "prod";

        var keyspace = new Keyspace(cluster, "cell-1", 42L);
        var cell = KeySegment.FromString("cell-1");
        var number = KeySegment.FromInt64(42);

        Assert.Equal(cluster, keyspace.Cluster);
        Assert.Equal<KeySegment>([cell, number], keyspace.Segments);
    }

    [Fact]
    public void Constructor_WhenThereAreNoSegments_Succeeds()
    {
        var keyspace = new Keyspace("prod");

        Assert.Empty(keyspace.Segments);
    }

    [Fact]
    public void Constructor_WhenClusterIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Keyspace(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Prod")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a_b")]
    [InlineData("a\n")]
    public void Constructor_WhenClusterIsInvalid_Throws(string cluster)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Keyspace(cluster));

        Assert.Equal("cluster", exception.ParamName);
        Assert.Contains("is not a cluster name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WhenClusterIsTooLong_Throws()
    {
        var cluster = new string('a', Keyspace.MaxClusterLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => new Keyspace(cluster));

        Assert.Equal("cluster", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenClusterHasMaxLength_Succeeds()
    {
        var cluster = new string('a', Keyspace.MaxClusterLength);

        var keyspace = new Keyspace(cluster);

        Assert.Equal(cluster, keyspace.Cluster);
    }

    [Fact]
    public void Constructor_WhenSegmentsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new Keyspace("prod", null!));

        Assert.Equal("segments", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenASegmentIsNull_Throws()
    {
        KeySegment[] segments = [KeySegment.FromInt64(1), null!];

        var exception = Assert.Throws<ArgumentException>(() => new Keyspace("prod", segments));

        Assert.Equal("segments", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreMaxSegments_Succeeds()
    {
        var segments = Enumerable
            .Range(1, Keyspace.MaxSegments)
            .Select(x => KeySegment.FromInt64(x))
            .ToArray();

        var keyspace = new Keyspace("prod", segments);

        Assert.Equal(Keyspace.MaxSegments, keyspace.Segments.Length);
    }

    [Fact]
    public void Constructor_WhenThereAreTooManySegments_Throws()
    {
        var segments = Enumerable
            .Range(1, Keyspace.MaxSegments + 1)
            .Select(x => KeySegment.FromInt64(x))
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new Keyspace("prod", segments));

        Assert.Equal("segments", exception.ParamName);
        Assert.Contains("at most 8", exception.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenClusterAndSegmentsMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new Keyspace("prod", "cell-1", 42L);
        var second = new Keyspace("prod", "cell-1", 42L);

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
        var keyspace = new Keyspace("prod", "cell-1");

        var equal = keyspace.Equals(keyspace);

        Assert.True(equal);
    }

    [Fact]
    public void Equals_WhenOtherIsNull_ReturnsFalse()
    {
        var keyspace = new Keyspace("prod");

        var equal = keyspace.Equals(null);

        Assert.False(equal);
    }

    [Fact]
    public void Equals_WhenClusterDiffers_ReturnsFalse()
    {
        var first = new Keyspace("prod", "cell-1");
        var second = new Keyspace("test", "cell-1");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_WhenSegmentOrderDiffers_ReturnsFalse()
    {
        var first = new Keyspace("prod", "a", "b");
        var second = new Keyspace("prod", "b", "a");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_WhenSegmentCountDiffers_ReturnsFalse()
    {
        var first = new Keyspace("prod", "a");
        var second = new Keyspace("prod", "a", "b");

        Assert.NotEqual(first, second);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_WhenThereAreSegments_JoinsThemWithSlashes()
    {
        var keyspace = new Keyspace("prod", "cell-1", 42L);

        var text = keyspace.ToString();

        Assert.Equal("prod/cell-1/42", text);
    }

    [Fact]
    public void ToString_WhenThereAreNoSegments_ReturnsTheCluster()
    {
        var keyspace = new Keyspace("prod");

        var text = keyspace.ToString();

        Assert.Equal("prod", text);
    }

    #endregion
}
