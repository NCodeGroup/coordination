// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Checking.Tests;

public sealed class LinearizedSetTests
{
    #region Empty Tests

    [Fact]
    public void Empty_WhenCreated_ContainsNoIndex()
    {
        const int count = 130;

        var set = LinearizedSet.Empty(count);

        for (var index = 0; index < count; index++)
        {
            Assert.False(set.Contains(index));
        }
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Add_WhenIndexIsAdded_ContainsIt()
    {
        var set = LinearizedSet.Empty(10).Add(3);

        Assert.True(set.Contains(3));
        Assert.False(set.Contains(2));
        Assert.False(set.Contains(4));
    }

    [Fact]
    public void Add_WhenIndexIsAdded_KeepsTheOriginal()
    {
        var original = LinearizedSet.Empty(10);

        var added = original.Add(3);

        Assert.False(original.Contains(3));
        Assert.True(added.Contains(3));
    }

    [Fact]
    public void Add_WhenIndexIsInASecondWord_ContainsOnlyThatIndex()
    {
        const int index = 70;

        var set = LinearizedSet.Empty(100).Add(index);

        Assert.True(set.Contains(index));
        Assert.False(set.Contains(index - 64));
        Assert.False(set.Contains(index - 1));
        Assert.False(set.Contains(index + 1));
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenSetsHoldTheSameIndexes_ReturnsTrueWithEqualHashes()
    {
        var left = LinearizedSet.Empty(100).Add(3).Add(70);
        var right = LinearizedSet.Empty(100).Add(70).Add(3);

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenSetsHoldDifferentIndexes_ReturnsFalse()
    {
        var left = LinearizedSet.Empty(100).Add(3);
        var right = LinearizedSet.Empty(100).Add(70);

        Assert.False(left.Equals(right));
        Assert.False(left.Equals((object)right));
    }

    [Fact]
    public void Equals_WhenSetsAreSizedDifferently_ReturnsFalse()
    {
        var left = LinearizedSet.Empty(64);
        var right = LinearizedSet.Empty(65);

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_WhenOtherIsNull_ReturnsFalse()
    {
        var set = LinearizedSet.Empty(10);

        Assert.False(set.Equals(null));
        Assert.False(set.Equals((object?)null));
    }

    #endregion
}
