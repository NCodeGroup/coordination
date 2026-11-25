// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class TokenBucketLimitTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheLimitIsValid_KeepsTheValues()
    {
        var limit = new TokenBucketLimit(4, 2, TimeSpan.FromSeconds(1));

        Assert.Equal(4, limit.TokenLimit);
        Assert.Equal(2, limit.TokensPerPeriod);
        Assert.Equal(TimeSpan.FromSeconds(1), limit.ReplenishmentPeriod);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Constructor_WhenTheTokensPerPeriodAreOutsideTheBounds_Throws(int tokensPerPeriod)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TokenBucketLimit(4, tokensPerPeriod, TimeSpan.FromSeconds(1))
        );

        Assert.Equal("tokensPerPeriod", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheBucketTakesLongerThanTheMostAllowedToFill_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new TokenBucketLimit(1_000, 1, TimeSpan.FromDays(1))
        );

        Assert.Equal("replenishmentPeriod", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheBucketFillsInExactlyTheMostAllowed_DoesNotThrow()
    {
        var limit = new TokenBucketLimit(180, 1, TimeSpan.FromDays(1));

        Assert.Equal(TimeSpan.FromDays(1), limit.ReplenishmentPeriod);
    }

    #endregion
}
