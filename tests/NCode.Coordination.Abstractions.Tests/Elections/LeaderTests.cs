// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class LeaderTests
{
    private static CoordinationKey Role() => new(new Keyspace("prod"), "workers");

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsAll()
    {
        var role = Role();
        var term = new FencingToken(7);
        var value = "10.0.0.4:5000"u8.ToArray();

        var leader = new Leader(role, term, value);

        Assert.Same(role, leader.Role);
        Assert.Same(term, leader.Term);
        var kept = leader.Value.ToArray();

        Assert.Equal<byte>(value, kept);
    }

    [Fact]
    public void Constructor_WhenValueIsAtTheLimit_Accepts()
    {
        var value = new byte[CoordinationLimits.MaxValueLength];

        var leader = new Leader(Role(), new FencingToken(7), value);

        Assert.Equal(65_536, leader.Value.Length);
    }

    [Fact]
    public void Constructor_WhenValueIsAboveTheLimit_Throws()
    {
        var value = new byte[CoordinationLimits.MaxValueLength + 1];

        var exception = Assert.Throws<ArgumentException>(() =>
            new Leader(Role(), new FencingToken(7), value)
        );

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenRoleIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Leader(null!, new FencingToken(7), ReadOnlyMemory<byte>.Empty)
        );

        Assert.Equal("role", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTermIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Leader(Role(), null!, ReadOnlyMemory<byte>.Empty)
        );

        Assert.Equal("term", exception.ParamName);
    }

    #endregion
}
