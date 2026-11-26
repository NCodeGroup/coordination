// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class FixedWindowLimitTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheArgumentsAreValid_KeepsTheValues()
    {
        var window = TimeSpan.FromSeconds(2);

        var limit = new FixedWindowLimit(10, window);

        Assert.Equal(10, limit.PermitLimit);
        Assert.Equal(window, limit.Window);
    }

    [Fact]
    public void Constructor_WhenThePermitLimitIsZero_Throws()
    {
        var window = TimeSpan.FromSeconds(2);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedWindowLimit(0, window)
        );

        Assert.Equal("permitLimit", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsZero_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedWindowLimit(10, TimeSpan.Zero)
        );

        Assert.Equal("window", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsLongerThanTheMostAllowed_Throws()
    {
        var window = TimeSpan.FromDays(181);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedWindowLimit(10, window)
        );

        Assert.Equal("window", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsNotWholeMilliseconds_Throws()
    {
        var window = TimeSpan.FromTicks(15_000);

        var exception = Assert.Throws<ArgumentException>(() => new FixedWindowLimit(10, window));

        Assert.Equal("window", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheWindowIsTheMostAllowed_KeepsTheWindow()
    {
        var window = TimeSpan.FromDays(180);

        var limit = new FixedWindowLimit(10, window);

        Assert.Equal(window, limit.Window);
    }

    #endregion
}
