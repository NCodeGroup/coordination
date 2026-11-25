// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SessionIdTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsBoth()
    {
        var keyspace = new Keyspace("prod");

        var id = new SessionId(keyspace, "7");

        Assert.Equal(keyspace, id.Keyspace);
        Assert.Equal("7", id.Value);
    }

    [Fact]
    public void Constructor_WhenKeyspaceIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SessionId(null!, "7"));

        Assert.Equal("keyspace", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenValueIsEmpty_Throws()
    {
        var keyspace = new Keyspace("prod");

        var exception = Assert.Throws<ArgumentException>(() => new SessionId(keyspace, ""));

        Assert.Equal("value", exception.ParamName);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenKeyspaceAndValueMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new SessionId(new Keyspace("prod"), "7");
        var second = new SessionId(new Keyspace("prod"), "7");

        var firstHashCode = first.GetHashCode();
        var secondHashCode = second.GetHashCode();

        Assert.Equal(first, second);
        Assert.Equal(firstHashCode, secondHashCode);
    }

    [Fact]
    public void Equals_WhenKeyspacesDiffer_ReturnsFalse()
    {
        var first = new SessionId(new Keyspace("prod"), "7");
        var second = new SessionId(new Keyspace("dev"), "7");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var first = new SessionId(new Keyspace("prod"), "7");
        var second = new SessionId(new Keyspace("prod"), "8");

        Assert.NotEqual(first, second);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_Always_JoinsKeyspaceAndValueWithAt()
    {
        var text = new SessionId(new Keyspace("prod"), "7").ToString();

        Assert.Equal("prod@7", text);
    }

    #endregion
}
