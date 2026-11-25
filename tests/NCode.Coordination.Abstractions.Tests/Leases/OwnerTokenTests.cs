// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class OwnerTokenTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenValueIsValid_KeepsValue()
    {
        var token = new OwnerToken("abc-123");

        Assert.Equal("abc-123", token.Value);
    }

    [Fact]
    public void Constructor_WhenValueIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OwnerToken(null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenValueHasASpace_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OwnerToken("a b"));

        Assert.Equal("value", exception.ParamName);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenValuesMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new OwnerToken("a");
        var second = new OwnerToken("a");

        var firstHashCode = first.GetHashCode();
        var secondHashCode = second.GetHashCode();

        Assert.Equal(first, second);
        Assert.Equal(firstHashCode, secondHashCode);
    }

    [Fact]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        Assert.NotEqual(new OwnerToken("a"), new OwnerToken("b"));
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_Always_ReturnsTheValue()
    {
        var text = new OwnerToken("abc").ToString();

        Assert.Equal("abc", text);
    }

    #endregion
}
