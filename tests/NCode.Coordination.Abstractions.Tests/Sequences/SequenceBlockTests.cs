// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SequenceBlockTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValuesAndComputesLast()
    {
        var block = new SequenceBlock(10, 5);

        Assert.Equal(10, block.First);
        Assert.Equal(5, block.Count);
        Assert.Equal(14, block.Last);
    }

    [Fact]
    public void Constructor_WhenCountIsOne_LastEqualsFirst()
    {
        var block = new SequenceBlock(7, 1);

        Assert.Equal(block.First, block.Last);
    }

    [Fact]
    public void Constructor_WhenFirstIsZero_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SequenceBlock(0, 1));

        Assert.Equal("first", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenCountIsZero_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SequenceBlock(1, 0));

        Assert.Equal("count", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenCountIsAboveTheLimit_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SequenceBlock(1, 65_537)
        );

        Assert.Equal("count", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenBlockPassesTheLargestNumber_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SequenceBlock(long.MaxValue, 2)
        );

        Assert.Equal("first", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenBlockEndsAtTheLargestNumber_Accepts()
    {
        var block = new SequenceBlock(long.MaxValue, 1);

        Assert.Equal(long.MaxValue, block.Last);
    }

    #endregion
}
