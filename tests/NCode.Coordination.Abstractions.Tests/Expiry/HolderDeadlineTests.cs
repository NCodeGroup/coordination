// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.Tests;

public sealed class HolderDeadlineTests
{
    private static FakeTimeProvider CreateTime()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return new FakeTimeProvider(start);
    }

    private static TimeSpan TenSecondSpan { get; } = TimeSpan.FromSeconds(10);

    private static TimeToLive TenSeconds { get; } = new(TenSecondSpan);

    #region Begin Tests

    [Fact]
    public void Begin_WhenAllowanceIsZero_LastsTheTimeToLive()
    {
        var time = CreateTime();

        var deadline = HolderDeadline.Begin(time, TenSeconds, TimeSpan.Zero);

        Assert.Equal(TenSecondSpan, deadline.Lifetime);
    }

    [Fact]
    public void Begin_WhenAllowanceIsGiven_ShortensTheLifetime()
    {
        var time = CreateTime();
        var allowance = TimeSpan.FromSeconds(1);

        var deadline = HolderDeadline.Begin(time, TenSeconds, allowance);

        var expected = TimeSpan.FromSeconds(9);
        Assert.Equal(expected, deadline.Lifetime);
    }

    [Fact]
    public void Begin_WhenAllowanceIsNegative_Throws()
    {
        var time = CreateTime();
        var allowance = TimeSpan.FromSeconds(-1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            HolderDeadline.Begin(time, TenSeconds, allowance)
        );

        Assert.Equal("driftAllowance", exception.ParamName);
    }

    [Fact]
    public void Begin_WhenAllowanceIsNotShorterThanTheTimeToLive_Throws()
    {
        var time = CreateTime();
        var allowance = TimeSpan.FromSeconds(10);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            HolderDeadline.Begin(time, TenSeconds, allowance)
        );

        Assert.Equal("driftAllowance", exception.ParamName);
    }

    [Fact]
    public void Begin_WhenTimeIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            HolderDeadline.Begin(null!, TenSeconds, TimeSpan.Zero)
        );

        Assert.Equal("time", exception.ParamName);
    }

    [Fact]
    public void Begin_WhenTimeToLiveIsNull_Throws()
    {
        var time = CreateTime();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            HolderDeadline.Begin(time, null!, TimeSpan.Zero)
        );

        Assert.Equal("timeToLive", exception.ParamName);
    }

    #endregion

    #region HasPassed Property Tests

    [Fact]
    public void HasPassed_BeforeTheLifetimeEnds_IsFalse()
    {
        var time = CreateTime();
        var allowance = TimeSpan.FromSeconds(1);
        var deadline = HolderDeadline.Begin(time, TenSeconds, allowance);
        var elapsed = TimeSpan.FromMilliseconds(8_999);

        time.Advance(elapsed);

        Assert.False(deadline.HasPassed);
    }

    [Fact]
    public void HasPassed_WhenTheLifetimeEnds_IsTrue()
    {
        var time = CreateTime();
        var allowance = TimeSpan.FromSeconds(1);
        var deadline = HolderDeadline.Begin(time, TenSeconds, allowance);
        var elapsed = TimeSpan.FromSeconds(9);

        time.Advance(elapsed);

        Assert.True(deadline.HasPassed);
    }

    #endregion

    #region Remaining Property Tests

    [Fact]
    public void Remaining_WhenTimePasses_CountsDown()
    {
        var time = CreateTime();
        var allowance = TimeSpan.FromSeconds(1);
        var deadline = HolderDeadline.Begin(time, TenSeconds, allowance);
        var elapsed = TimeSpan.FromSeconds(4);

        time.Advance(elapsed);

        var expected = TimeSpan.FromSeconds(5);
        Assert.Equal(expected, deadline.Remaining);
    }

    [Fact]
    public void Remaining_AfterTheDeadline_IsZero()
    {
        var time = CreateTime();
        var allowance = TimeSpan.FromSeconds(1);
        var deadline = HolderDeadline.Begin(time, TenSeconds, allowance);
        var elapsed = TimeSpan.FromSeconds(20);

        time.Advance(elapsed);

        Assert.Equal(TimeSpan.Zero, deadline.Remaining);
    }

    #endregion
}
