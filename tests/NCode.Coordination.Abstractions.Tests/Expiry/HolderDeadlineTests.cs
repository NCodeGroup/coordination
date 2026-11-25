// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.Tests;

public sealed class HolderDeadlineTests
{
    private static FakeTimeProvider CreateTime() =>
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static TimeToLive TenSeconds { get; } = new(TimeSpan.FromSeconds(10));

    #region Begin Tests

    [Fact]
    public void Begin_WhenAllowanceIsZero_LastsTheTimeToLive()
    {
        var time = CreateTime();

        var deadline = HolderDeadline.Begin(time, TenSeconds, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(10), deadline.Lifetime);
    }

    [Fact]
    public void Begin_WhenAllowanceIsGiven_ShortensTheLifetime()
    {
        var time = CreateTime();

        var deadline = HolderDeadline.Begin(time, TenSeconds, TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(9), deadline.Lifetime);
    }

    [Fact]
    public void Begin_WhenAllowanceIsNegative_Throws()
    {
        var time = CreateTime();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            HolderDeadline.Begin(time, TenSeconds, TimeSpan.FromSeconds(-1))
        );

        Assert.Equal("driftAllowance", exception.ParamName);
    }

    [Fact]
    public void Begin_WhenAllowanceIsNotShorterThanTheTimeToLive_Throws()
    {
        var time = CreateTime();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            HolderDeadline.Begin(time, TenSeconds, TimeSpan.FromSeconds(10))
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
        var deadline = HolderDeadline.Begin(time, TenSeconds, TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromMilliseconds(8_999));

        Assert.False(deadline.HasPassed);
    }

    [Fact]
    public void HasPassed_WhenTheLifetimeEnds_IsTrue()
    {
        var time = CreateTime();
        var deadline = HolderDeadline.Begin(time, TenSeconds, TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(9));

        Assert.True(deadline.HasPassed);
    }

    #endregion

    #region Remaining Property Tests

    [Fact]
    public void Remaining_WhenTimePasses_CountsDown()
    {
        var time = CreateTime();
        var deadline = HolderDeadline.Begin(time, TenSeconds, TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(TimeSpan.FromSeconds(5), deadline.Remaining);
    }

    [Fact]
    public void Remaining_AfterTheDeadline_IsZero()
    {
        var time = CreateTime();
        var deadline = HolderDeadline.Begin(time, TenSeconds, TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.Zero, deadline.Remaining);
    }

    #endregion
}
