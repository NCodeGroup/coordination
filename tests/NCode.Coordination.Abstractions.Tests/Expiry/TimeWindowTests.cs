// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class TimeWindowTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset Ten = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenAligned_KeepsStartAndLength()
    {
        var window = new TimeWindow(Ten, OneHour);

        Assert.Equal(Ten, window.Start);
        Assert.Equal(OneHour, window.Length);
    }

    [Fact]
    public void Constructor_WhenStartIsNotAMultipleOfLength_Throws()
    {
        var start = Ten.AddMinutes(30);

        var exception = Assert.Throws<ArgumentException>(() => new TimeWindow(start, OneHour));

        Assert.Equal("start", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenStartIsNotUtc_Throws()
    {
        var start = Ten.ToOffset(TimeSpan.FromHours(1));

        var exception = Assert.Throws<ArgumentException>(() => new TimeWindow(start, OneHour));

        Assert.Equal("start", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenStartIsBeforeEpoch_Throws()
    {
        var start = DateTimeOffset.UnixEpoch.AddHours(-1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeWindow(start, OneHour)
        );

        Assert.Equal("start", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenWindowEndsBeyondLargestInstant_Throws()
    {
        var start = DateTimeOffset.MaxValue;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeWindow(start, OneHour)
        );

        Assert.Equal("start", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenLengthIsZero_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeWindow(Ten, TimeSpan.Zero)
        );

        Assert.Equal("length", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenLengthIsNegative_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeWindow(Ten, -OneHour)
        );

        Assert.Equal("length", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenLengthIsOverMaxDuration_Throws()
    {
        var length = TimeToLive.MaxDuration + TimeSpan.FromTicks(1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeWindow(Ten, length)
        );

        Assert.Equal("length", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenLengthIsNotWholeMilliseconds_Throws()
    {
        var length = TimeSpan.FromTicks(15_000);

        var exception = Assert.Throws<ArgumentException>(() => new TimeWindow(Ten, length));

        Assert.Equal("length", exception.ParamName);
    }

    #endregion

    #region Containing Tests

    [Fact]
    public void Containing_WhenInstantIsInsideWindow_ReturnsAlignedWindow()
    {
        var instant = Ten.AddMinutes(7).AddSeconds(30);

        var window = TimeWindow.Containing(instant, OneHour);

        Assert.Equal(Ten, window.Start);
        Assert.Equal(Ten.AddHours(1), window.End);
    }

    [Fact]
    public void Containing_WhenInstantHasAnOffset_ReturnsTheSameWindow()
    {
        var instant = new DateTimeOffset(2026, 11, 25, 12, 7, 30, TimeSpan.FromHours(2));

        var window = TimeWindow.Containing(instant, OneHour);

        Assert.Equal(TimeWindow.Containing(Ten.AddMinutes(7).AddSeconds(30), OneHour), window);
    }

    [Fact]
    public void Containing_WhenInstantIsOnBoundary_StartsAtInstant()
    {
        var window = TimeWindow.Containing(Ten, OneHour);

        Assert.Equal(Ten, window.Start);
    }

    [Fact]
    public void Containing_WhenInstantIsBeforeEpoch_Throws()
    {
        var instant = DateTimeOffset.UnixEpoch.AddTicks(-1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimeWindow.Containing(instant, OneHour)
        );

        Assert.Equal("instant", exception.ParamName);
    }

    [Fact]
    public void Containing_WhenLengthIsZero_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimeWindow.Containing(Ten, TimeSpan.Zero)
        );

        Assert.Equal("length", exception.ParamName);
    }

    [Fact]
    public void Containing_WhenLengthIsNotWholeMilliseconds_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TimeWindow.Containing(Ten, TimeSpan.FromTicks(15_000))
        );

        Assert.Equal("length", exception.ParamName);
    }

    #endregion

    #region Segment Property Tests

    [Fact]
    public void Segment_Always_IsTheStartInUnixMilliseconds()
    {
        var window = new TimeWindow(Ten, OneHour);

        Assert.Equal(KeySegment.FromInt64(Ten.ToUnixTimeMilliseconds()), window.Segment);
    }

    #endregion

    #region TryCreateTimeToLive Tests

    [Fact]
    public void TryCreateTimeToLive_WhenWindowIsOpen_ReturnsTimeToLiveToEndPlusAllowance()
    {
        var window = new TimeWindow(Ten, OneHour);

        var created = window.TryCreateTimeToLive(
            Ten.AddMinutes(30),
            TimeSpan.FromMinutes(1),
            out var timeToLive
        );

        Assert.True(created);
        Assert.NotNull(timeToLive);
        Assert.Equal(TimeSpan.FromMinutes(31), timeToLive.Duration);
    }

    [Fact]
    public void TryCreateTimeToLive_WhenWindowAndAllowanceHavePassed_ReturnsFalse()
    {
        var window = new TimeWindow(Ten, OneHour);

        var created = window.TryCreateTimeToLive(
            Ten.AddHours(1).AddMinutes(2),
            TimeSpan.FromMinutes(1),
            out var timeToLive
        );

        Assert.False(created);
        Assert.Null(timeToLive);
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Equals_WhenStartAndLengthMatch_IsEqual()
    {
        var left = new TimeWindow(Ten, OneHour);
        var right = TimeWindow.Containing(Ten.AddMinutes(5), OneHour);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_Always_ReturnsStartAndLength()
    {
        var window = new TimeWindow(Ten, OneHour);

        Assert.Equal("2026-11-25T10:00:00.0000000+00:00/01:00:00", window.ToString());
    }

    #endregion
}
