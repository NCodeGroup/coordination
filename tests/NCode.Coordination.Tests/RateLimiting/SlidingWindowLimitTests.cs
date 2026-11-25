// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SlidingWindowLimitTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheWindowDividesEvenly_KeepsTheValuesAndSplitsTheWindow()
    {
        var window = TimeSpan.FromSeconds(2);
        var segmentLength = TimeSpan.FromSeconds(1);
        var limit = new SlidingWindowLimit(10, window, 2);

        Assert.Equal(10, limit.PermitLimit);
        Assert.Equal(window, limit.Window);
        Assert.Equal(2, limit.SegmentsPerWindow);
        Assert.Equal(segmentLength, limit.SegmentLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SlidingWindowLimit.MaxSegmentsPerWindow + 1)]
    public void Constructor_WhenTheSegmentCountIsOutsideTheBounds_Throws(int segmentsPerWindow)
    {
        var window = TimeSpan.FromSeconds(2);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowLimit(10, window, segmentsPerWindow)
        );

        Assert.Equal("segmentsPerWindow", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowDoesNotDivideIntoWholeMilliseconds_Throws()
    {
        var window = TimeSpan.FromMilliseconds(1_001);

        var exception = Assert.Throws<ArgumentException>(() =>
            new SlidingWindowLimit(10, window, 2)
        );

        Assert.Equal("segmentsPerWindow", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsNotWholeMilliseconds_Throws()
    {
        var window = TimeSpan.FromTicks(15_000);

        var exception = Assert.Throws<ArgumentException>(() =>
            new SlidingWindowLimit(10, window, 1)
        );

        Assert.Equal("window", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsLongerThanTheMostAllowed_Throws()
    {
        var window = TimeSpan.FromDays(181);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowLimit(10, window, 2)
        );

        Assert.Equal("window", exception.ParamName);
    }

    #endregion
}
