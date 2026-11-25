// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class CounterIncrementTests
{
    private static CoordinationKey Key() => new(new Keyspace("prod"), "rate");

    private static TimeToLive Ttl()
    {
        var duration = TimeSpan.FromMinutes(1);

        return new TimeToLive(duration);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsAll()
    {
        var key = Key();
        var timeToLive = Ttl();

        var increment = new CounterIncrement(key, 3, 10, timeToLive);

        Assert.Same(key, increment.Key);
        Assert.Equal(3, increment.Amount);
        Assert.Equal(10, increment.Limit);
        Assert.Same(timeToLive, increment.TimeToLive);
    }

    [Fact]
    public void Constructor_WhenLimitIsZero_Accepts()
    {
        var key = Key();
        var timeToLive = Ttl();

        var increment = new CounterIncrement(key, 1, 0, timeToLive);

        Assert.Equal(0, increment.Limit);
    }

    [Fact]
    public void Constructor_WhenAmountIsOne_Accepts()
    {
        var key = Key();
        var timeToLive = Ttl();

        var increment = new CounterIncrement(key, 1, 10, timeToLive);

        Assert.Equal(1, increment.Amount);
    }

    [Fact]
    public void Constructor_WhenAmountIsZero_Throws()
    {
        var key = Key();
        var timeToLive = Ttl();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CounterIncrement(key, 0, 10, timeToLive)
        );

        Assert.Equal("amount", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenLimitIsNegative_Throws()
    {
        var key = Key();
        var timeToLive = Ttl();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CounterIncrement(key, 1, -1, timeToLive)
        );

        Assert.Equal("limit", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenKeyIsNull_Throws()
    {
        var timeToLive = Ttl();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CounterIncrement(null!, 1, 10, timeToLive)
        );

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTimeToLiveIsNull_Throws()
    {
        var key = Key();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CounterIncrement(key, 1, 10, null!)
        );

        Assert.Equal("timeToLive", exception.ParamName);
    }

    #endregion
}
