// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class TimeToLiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenDurationIsValid_KeepsDuration()
    {
        var duration = TimeSpan.FromMinutes(5);

        var timeToLive = new TimeToLive(duration);

        Assert.Equal(duration, timeToLive.Duration);
    }

    [Fact]
    public void Constructor_WhenDurationIsZero_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeToLive(TimeSpan.Zero)
        );

        Assert.Equal("duration", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenDurationIsNegative_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeToLive(TimeSpan.FromSeconds(-1))
        );

        Assert.Equal("duration", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenDurationIsMaxDuration_KeepsDuration()
    {
        var timeToLive = new TimeToLive(TimeToLive.MaxDuration);

        Assert.Equal(TimeToLive.MaxDuration, timeToLive.Duration);
    }

    [Fact]
    public void Constructor_WhenDurationIsOneTickOverMaxDuration_Throws()
    {
        var duration = TimeToLive.MaxDuration + TimeSpan.FromTicks(1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new TimeToLive(duration));

        Assert.Equal("duration", exception.ParamName);
    }

    #endregion

    #region TryCreateUntil Tests

    [Fact]
    public void TryCreateUntil_WhenInstantIsAhead_AddsAllowance()
    {
        var instant = Now.AddMinutes(10);

        var created = TimeToLive.TryCreateUntil(
            instant,
            Now,
            TimeSpan.FromMinutes(1),
            out var timeToLive
        );

        Assert.True(created);
        Assert.NotNull(timeToLive);
        var expected = TimeSpan.FromMinutes(11);

        Assert.Equal(expected, timeToLive.Duration);
    }

    [Fact]
    public void TryCreateUntil_WhenInstantIsPastWithinAllowance_ReturnsRemainder()
    {
        var instant = Now.AddSeconds(-30);

        var created = TimeToLive.TryCreateUntil(
            instant,
            Now,
            TimeSpan.FromMinutes(1),
            out var timeToLive
        );

        Assert.True(created);
        Assert.NotNull(timeToLive);
        var expected = TimeSpan.FromSeconds(30);

        Assert.Equal(expected, timeToLive.Duration);
    }

    [Fact]
    public void TryCreateUntil_WhenInstantIsPastBeyondAllowance_ReturnsFalse()
    {
        var instant = Now.AddMinutes(-2);

        var created = TimeToLive.TryCreateUntil(
            instant,
            Now,
            TimeSpan.FromMinutes(1),
            out var timeToLive
        );

        Assert.False(created);
        Assert.Null(timeToLive);
    }

    [Fact]
    public void TryCreateUntil_WhenRemainingIsZero_ReturnsFalse()
    {
        var instant = Now.AddMinutes(-1);

        var created = TimeToLive.TryCreateUntil(
            instant,
            Now,
            TimeSpan.FromMinutes(1),
            out var timeToLive
        );

        Assert.False(created);
        Assert.Null(timeToLive);
    }

    [Fact]
    public void TryCreateUntil_WhenAllowanceIsNegative_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimeToLive.TryCreateUntil(Now.AddMinutes(1), Now, TimeSpan.FromSeconds(-1), out _)
        );

        Assert.Equal("skewAllowance", exception.ParamName);
    }

    [Fact]
    public void TryCreateUntil_WhenAllowanceIsTooLarge_Throws()
    {
        var allowance = TimeToLive.MaxDuration + TimeSpan.FromTicks(1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimeToLive.TryCreateUntil(Now.AddMinutes(-1), Now, allowance, out _)
        );

        Assert.Equal("skewAllowance", exception.ParamName);
    }

    [Fact]
    public void TryCreateUntil_WhenInstantIsBeyondMaxDuration_Throws()
    {
        var instant = Now.AddDays(400);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimeToLive.TryCreateUntil(instant, Now, TimeSpan.Zero, out _)
        );

        Assert.Equal("instant", exception.ParamName);
        Assert.Contains("longest time to live", exception.Message, StringComparison.Ordinal);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ReturnsInvariantDuration()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(5));

        var text = timeToLive.ToString();

        Assert.Equal("00:05:00", text);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenDurationsAreEqual_ReturnsTrue()
    {
        var first = new TimeToLive(TimeSpan.FromMinutes(5));
        var second = new TimeToLive(TimeSpan.FromMinutes(5));

        var firstHashCode = first.GetHashCode();
        var secondHashCode = second.GetHashCode();

        Assert.Equal(first, second);
        Assert.Equal(firstHashCode, secondHashCode);
    }

    [Fact]
    public void Equals_WhenDurationsDiffer_ReturnsFalse()
    {
        var first = new TimeToLive(TimeSpan.FromMinutes(5));
        var second = new TimeToLive(TimeSpan.FromMinutes(6));

        Assert.NotEqual(first, second);
    }

    #endregion
}
