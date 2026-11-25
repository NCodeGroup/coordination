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
            var contains = set.Contains(index);

            Assert.False(contains);
        }
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Add_WhenIndexIsAdded_ContainsIt()
    {
        var set = LinearizedSet.Empty(10).Add(3);

        var containsThree = set.Contains(3);
        var containsTwo = set.Contains(2);
        var containsFour = set.Contains(4);

        Assert.True(containsThree);
        Assert.False(containsTwo);
        Assert.False(containsFour);
    }

    [Fact]
    public void Add_WhenIndexIsAdded_KeepsTheOriginal()
    {
        var original = LinearizedSet.Empty(10);

        var added = original.Add(3);

        var originalContains = original.Contains(3);
        var addedContains = added.Contains(3);

        Assert.False(originalContains);
        Assert.True(addedContains);
    }

    [Fact]
    public void Add_WhenIndexIsInASecondWord_ContainsOnlyThatIndex()
    {
        const int index = 70;

        var set = LinearizedSet.Empty(100).Add(index);

        var containsIndex = set.Contains(index);
        var containsFirstWordIndex = set.Contains(index - 64);
        var containsPrevious = set.Contains(index - 1);
        var containsNext = set.Contains(index + 1);

        Assert.True(containsIndex);
        Assert.False(containsFirstWordIndex);
        Assert.False(containsPrevious);
        Assert.False(containsNext);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenSetsHoldTheSameIndexes_ReturnsTrueWithEqualHashes()
    {
        var left = LinearizedSet.Empty(100).Add(3).Add(70);
        var right = LinearizedSet.Empty(100).Add(70).Add(3);

        var equalsSet = left.Equals(right);
        var equalsObject = left.Equals((object)right);
        var leftHash = left.GetHashCode();
        var rightHash = right.GetHashCode();

        Assert.True(equalsSet);
        Assert.True(equalsObject);
        Assert.Equal(leftHash, rightHash);
    }

    [Fact]
    public void Equals_WhenSetsHoldDifferentIndexes_ReturnsFalse()
    {
        var left = LinearizedSet.Empty(100).Add(3);
        var right = LinearizedSet.Empty(100).Add(70);

        var equalsSet = left.Equals(right);
        var equalsObject = left.Equals((object)right);

        Assert.False(equalsSet);
        Assert.False(equalsObject);
    }

    [Fact]
    public void Equals_WhenSetsAreSizedDifferently_ReturnsFalse()
    {
        var left = LinearizedSet.Empty(64);
        var right = LinearizedSet.Empty(65);

        var equalsSet = left.Equals(right);

        Assert.False(equalsSet);
    }

    [Fact]
    public void Equals_WhenOtherIsNull_ReturnsFalse()
    {
        var set = LinearizedSet.Empty(10);
        var copy = LinearizedSet.Empty(10);

        var equalsNull = set.Equals(null);
        var equalsNullObject = copy.Equals((object?)null);

        Assert.False(equalsNull);
        Assert.False(equalsNullObject);
    }

    #endregion
}
