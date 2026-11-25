// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Recipes.Tests;

public sealed class RecipeKeysTests
{
    private static Keyspace TestKeyspace { get; } = new("prod", "cell-1");

    private static CoordinationKey CreateKey(int segmentCount)
    {
        var segments = new KeySegment[segmentCount];
        for (var i = 0; i < segmentCount; i++)
        {
            segments[i] = $"segment-{i}";
        }

        return new CoordinationKey(TestKeyspace, segments);
    }

    #region Child Tests

    [Fact]
    public void Child_WhenTheKeyHasRoom_AddsTheSegmentAndKeepsThePartitionDepth()
    {
        var key = CreateKey(2) with { PartitionDepth = 1 };
        KeySegment segment = "writer";

        var child = RecipeKeys.Child(key, segment, "resource");

        Assert.Equal(3, child.Segments.Length);
        Assert.Equal(segment, child.Segments[^1]);
        Assert.Equal(1, child.PartitionDepth);
        Assert.Same(TestKeyspace, child.Keyspace);
    }

    [Fact]
    public void Child_WhenTheKeyHasTheMostSegments_ThrowsNamingTheParameter()
    {
        var key = CreateKey(CoordinationKey.MaxSegments);

        var exception = Assert.Throws<ArgumentException>(() =>
            RecipeKeys.Child(key, "writer", "resource")
        );

        Assert.Equal("resource", exception.ParamName);
    }

    #endregion

    #region ThrowIfFull Tests

    [Fact]
    public void ThrowIfFull_WhenTheKeyHasRoom_DoesNotThrow()
    {
        var key = CreateKey(CoordinationKey.MaxSegments - 1);

        var exception = Record.Exception(() => RecipeKeys.ThrowIfFull(key, "resource"));

        Assert.Null(exception);
    }

    #endregion
}
