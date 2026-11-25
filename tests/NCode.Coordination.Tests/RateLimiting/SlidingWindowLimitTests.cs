// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SlidingWindowLimitTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheWindowDividesEvenly_KeepsTheValuesAndSplitsTheWindow()
    {
        var limit = new SlidingWindowLimit(10, TimeSpan.FromSeconds(2), 2);

        Assert.Equal(10, limit.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(2), limit.Window);
        Assert.Equal(2, limit.SegmentsPerWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), limit.SegmentLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SlidingWindowLimit.MaxSegmentsPerWindow + 1)]
    public void Constructor_WhenTheSegmentCountIsOutsideTheBounds_Throws(int segmentsPerWindow)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowLimit(10, TimeSpan.FromSeconds(2), segmentsPerWindow)
        );

        Assert.Equal("segmentsPerWindow", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowDoesNotDivideIntoWholeMilliseconds_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SlidingWindowLimit(10, TimeSpan.FromMilliseconds(1_001), 2)
        );

        Assert.Equal("segmentsPerWindow", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsNotWholeMilliseconds_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SlidingWindowLimit(10, TimeSpan.FromTicks(15_000), 1)
        );

        Assert.Equal("window", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsLongerThanTheMostAllowed_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowLimit(10, TimeSpan.FromDays(181), 2)
        );

        Assert.Equal("window", exception.ParamName);
    }

    #endregion
}
