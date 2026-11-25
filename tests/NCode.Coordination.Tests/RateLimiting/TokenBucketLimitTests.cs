// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class TokenBucketLimitTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheLimitIsValid_KeepsTheValues()
    {
        var period = TimeSpan.FromSeconds(1);
        var limit = new TokenBucketLimit(4, 2, period);

        Assert.Equal(4, limit.TokenLimit);
        Assert.Equal(2, limit.TokensPerPeriod);
        Assert.Equal(period, limit.ReplenishmentPeriod);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Constructor_WhenTheTokensPerPeriodAreOutsideTheBounds_Throws(int tokensPerPeriod)
    {
        var period = TimeSpan.FromSeconds(1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TokenBucketLimit(4, tokensPerPeriod, period)
        );

        Assert.Equal("tokensPerPeriod", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheBucketTakesLongerThanTheMostAllowedToFill_Throws()
    {
        var period = TimeSpan.FromDays(1);

        var exception = Assert.Throws<ArgumentException>(() =>
            new TokenBucketLimit(1_000, 1, period)
        );

        Assert.Equal("replenishmentPeriod", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheBucketFillsInExactlyTheMostAllowed_DoesNotThrow()
    {
        var period = TimeSpan.FromDays(1);
        var limit = new TokenBucketLimit(180, 1, period);

        Assert.Equal(period, limit.ReplenishmentPeriod);
    }

    #endregion
}
