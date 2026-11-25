// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class FencingTokenTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenValueIsPositive_KeepsValue()
    {
        var token = new FencingToken(7);

        Assert.Equal(7, token.Value);
    }

    [Fact]
    public void Constructor_WhenValueIsMaximum_Succeeds()
    {
        var token = new FencingToken(long.MaxValue);

        Assert.Equal(long.MaxValue, token.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenValueIsBelowOne_Throws(long value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FencingToken(value));

        Assert.Equal("value", exception.ParamName);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenValuesMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new FencingToken(5);
        var second = new FencingToken(5);

        var firstHashCode = first.GetHashCode();
        var secondHashCode = second.GetHashCode();

        Assert.Equal(first, second);
        Assert.Equal(firstHashCode, secondHashCode);
    }

    [Fact]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        Assert.NotEqual(new FencingToken(5), new FencingToken(6));
    }

    #endregion

    #region CompareTo Tests

    [Fact]
    public void CompareTo_WhenOtherIsGreater_ReturnsNegative()
    {
        var result = new FencingToken(1).CompareTo(new FencingToken(2));

        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTo_WhenOtherIsEqual_ReturnsZero()
    {
        var result = new FencingToken(2).CompareTo(new FencingToken(2));

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareTo_WhenOtherIsLess_ReturnsPositive()
    {
        var result = new FencingToken(3).CompareTo(new FencingToken(2));

        Assert.True(result > 0);
    }

    [Fact]
    public void CompareTo_WhenOtherIsNull_ReturnsOne()
    {
        var result = new FencingToken(3).CompareTo(null);

        Assert.Equal(1, result);
    }

    #endregion

    #region Operator Tests

    [Fact]
    public void Operators_WhenBothHaveValues_OrderByValue()
    {
        var low = new FencingToken(1);
        var high = new FencingToken(2);

        Assert.True(low < high);
        Assert.False(high < low);
        Assert.True(high > low);
        Assert.False(low > high);
        Assert.True(low <= high);
        Assert.False(high <= low);
        Assert.True(high >= low);
        Assert.False(low >= high);
        Assert.True(low <= new FencingToken(1));
        Assert.True(low >= new FencingToken(1));
    }

    [Fact]
    public void Operators_WhenLeftIsNull_OrdersNullBelow()
    {
        var token = new FencingToken(1);

        Assert.True(null < token);
        Assert.False(null > token);
        Assert.True(null <= token);
        Assert.False(null >= token);
    }

    [Fact]
    public void Operators_WhenRightIsNull_OrdersNullBelow()
    {
        var token = new FencingToken(1);

        Assert.False(token < null);
        Assert.True(token > null);
        Assert.False(token <= null);
        Assert.True(token >= null);
    }

    [Fact]
    public void Operators_WhenBothAreNull_AreEqual()
    {
        FencingToken? left = null;
        FencingToken? right = null;

        Assert.False(left < right);
        Assert.False(left > right);
        Assert.True(left <= right);
        Assert.True(left >= right);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_Always_ReturnsTheNumber()
    {
        var text = new FencingToken(42).ToString();

        Assert.Equal("42", text);
    }

    #endregion
}
