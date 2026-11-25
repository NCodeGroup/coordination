// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class CounterBatchResultTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenCountsAreValid_KeepsCounts()
    {
        var result = new CounterBatchResult([3, 5, 9]);

        Assert.Equal<long>([3, 5, 9], result.Counts);
    }

    [Fact]
    public void Constructor_WhenCountsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CounterBatchResult(null!));

        Assert.Equal("counts", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenCountsAreEmpty_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new CounterBatchResult([]));

        Assert.Equal("counts", exception.ParamName);
    }

    #endregion

    #region ExceededIndex Property Tests

    [Fact]
    public void ExceededIndex_ByDefault_IsNull()
    {
        var result = new CounterBatchResult([3, 5]);

        Assert.Null(result.ExceededIndex);
    }

    [Fact]
    public void ExceededIndex_WhenLastIndex_Accepts()
    {
        var result = new CounterBatchResult([3, 5]) { ExceededIndex = 1 };

        Assert.Equal(1, result.ExceededIndex);
    }

    [Fact]
    public void ExceededIndex_WhenEarlierIndex_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CounterBatchResult([3, 5]) { ExceededIndex = 0 }
        );

        Assert.Equal("ExceededIndex", exception.ParamName);
    }

    [Fact]
    public void ExceededIndex_WhenNegative_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CounterBatchResult([3, 5]) { ExceededIndex = -1 }
        );

        Assert.Equal("ExceededIndex", exception.ParamName);
    }

    #endregion
}
