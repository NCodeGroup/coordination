// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RevisionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenValueIsPositive_KeepsValue()
    {
        var revision = new Revision(7);

        Assert.Equal(7, revision.Value);
    }

    [Fact]
    public void Constructor_WhenValueIsMaximum_Succeeds()
    {
        var revision = new Revision(long.MaxValue);

        Assert.Equal(long.MaxValue, revision.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenValueIsBelowOne_Throws(long value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Revision(value));

        Assert.Equal("value", exception.ParamName);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenValuesMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new Revision(5);
        var second = new Revision(5);

        var firstHashCode = first.GetHashCode();
        var secondHashCode = second.GetHashCode();

        Assert.Equal(first, second);
        Assert.Equal(firstHashCode, secondHashCode);
    }

    [Fact]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        Assert.NotEqual(new Revision(5), new Revision(6));
    }

    #endregion

    #region CompareTo Tests

    [Fact]
    public void CompareTo_WhenOtherIsGreater_ReturnsNegative()
    {
        var result = new Revision(1).CompareTo(new Revision(2));

        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTo_WhenOtherIsEqual_ReturnsZero()
    {
        var result = new Revision(2).CompareTo(new Revision(2));

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareTo_WhenOtherIsLess_ReturnsPositive()
    {
        var result = new Revision(3).CompareTo(new Revision(2));

        Assert.True(result > 0);
    }

    [Fact]
    public void CompareTo_WhenOtherIsNull_ReturnsOne()
    {
        var result = new Revision(3).CompareTo(null);

        Assert.Equal(1, result);
    }

    #endregion

    #region Operator Tests

    [Fact]
    public void Operators_WhenBothHaveValues_OrderByValue()
    {
        var low = new Revision(1);
        var high = new Revision(2);

        Assert.True(low < high);
        Assert.False(high < low);
        Assert.True(high > low);
        Assert.False(low > high);
        Assert.True(low <= high);
        Assert.False(high <= low);
        Assert.True(high >= low);
        Assert.False(low >= high);
        Assert.True(low <= new Revision(1));
        Assert.True(low >= new Revision(1));
    }

    [Fact]
    public void Operators_WhenLeftIsNull_OrdersNullBelow()
    {
        var revision = new Revision(1);

        Assert.True(null < revision);
        Assert.False(null > revision);
        Assert.True(null <= revision);
        Assert.False(null >= revision);
    }

    [Fact]
    public void Operators_WhenRightIsNull_OrdersNullBelow()
    {
        var revision = new Revision(1);

        Assert.False(revision < null);
        Assert.True(revision > null);
        Assert.False(revision <= null);
        Assert.True(revision >= null);
    }

    [Fact]
    public void Operators_WhenBothAreNull_AreEqual()
    {
        Revision? left = null;
        Revision? right = null;

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
        var text = new Revision(42).ToString();

        Assert.Equal("42", text);
    }

    #endregion
}
