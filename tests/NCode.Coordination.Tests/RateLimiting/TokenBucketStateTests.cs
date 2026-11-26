// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class TokenBucketStateTests
{
    private const long NowMilliseconds = 1767225600000;

    private static TimeSpan ReplenishmentPeriod { get; } = TimeSpan.FromSeconds(1);

    private static TokenBucketLimit Limit { get; } = new(4, 2, ReplenishmentPeriod);

    #region Encode Tests

    [Fact]
    public void Encode_WhenTheStateHasValues_WritesTwoBigEndianIntegers()
    {
        var state = new TokenBucketState(3, NowMilliseconds);

        var value = state.Encode();

        Assert.Equal(
            [
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x03,
                0x00,
                0x00,
                0x01,
                0x9B,
                0x76,
                0xDA,
                0xA8,
                0x00,
            ],
            value
        );
    }

    #endregion

    #region Decode Tests

    [Fact]
    public void Decode_WhenTheValueWasEncoded_ReturnsTheState()
    {
        var state = new TokenBucketState(3, NowMilliseconds);

        var encoded = state.Encode();

        var decoded = TokenBucketState.Decode(encoded);

        Assert.Equal(state, decoded);
    }

    [Fact]
    public void Decode_WhenTheValueIsShort_Throws()
    {
        var value = new byte[TokenBucketState.EncodedLength - 1];

        var exception = Assert.Throws<InvalidDataException>(() => TokenBucketState.Decode(value));

        Assert.Equal("A token bucket's state is 16 bytes.", exception.Message);
    }

    [Fact]
    public void Decode_WhenTheTokensAreNegative_Throws()
    {
        var value = new TokenBucketState(-1, NowMilliseconds).Encode();

        var exception = Assert.Throws<InvalidDataException>(() => TokenBucketState.Decode(value));

        Assert.Equal("A token bucket's state holds no negative number.", exception.Message);
    }

    #endregion

    #region Replenish Tests

    [Fact]
    public void Replenish_WhenOnePeriodAndAHalfHavePassed_ReturnsOnePeriodOfTokens()
    {
        var state = new TokenBucketState(0, NowMilliseconds);

        var replenished = state.Replenish(Limit, NowMilliseconds + 1500);

        var expected = new TokenBucketState(2, NowMilliseconds + 1000);
        Assert.Equal(expected, replenished);
    }

    [Fact]
    public void Replenish_WhenEnoughPeriodsHavePassedToFill_ReturnsAFullBucketAtTheInstant()
    {
        var state = new TokenBucketState(0, NowMilliseconds);

        var replenished = state.Replenish(Limit, NowMilliseconds + 2500);

        var expected = new TokenBucketState(4, NowMilliseconds + 2500);
        Assert.Equal(expected, replenished);
    }

    [Fact]
    public void Replenish_WhenNoWholePeriodHasPassed_ReturnsTheState()
    {
        var state = new TokenBucketState(1, NowMilliseconds);

        var replenished = state.Replenish(Limit, NowMilliseconds + 999);

        Assert.Equal(state, replenished);
    }

    [Fact]
    public void Replenish_WhenTheBucketIsFull_ReturnsAFullBucketAtTheInstant()
    {
        var state = new TokenBucketState(4, NowMilliseconds);

        var replenished = state.Replenish(Limit, NowMilliseconds + 5000);

        var expected = new TokenBucketState(4, NowMilliseconds + 5000);
        Assert.Equal(expected, replenished);
    }

    #endregion

    #region GetRetryAfter Tests

    [Fact]
    public void GetRetryAfter_WhenTheBucketHoldsTooFewTokens_ReturnsTheTimeUntilThePeriodThatReturnsEnough()
    {
        var state = new TokenBucketState(1, NowMilliseconds);

        var retryAfter = state.GetRetryAfter(Limit, 4, NowMilliseconds + 200);

        var expected = TimeSpan.FromMilliseconds(1800);
        Assert.Equal(expected, retryAfter);
    }

    [Fact]
    public void GetRetryAfter_WhenTheBucketHoldsEnoughTokens_ReturnsZero()
    {
        var state = new TokenBucketState(3, NowMilliseconds);

        var retryAfter = state.GetRetryAfter(Limit, 3, NowMilliseconds);

        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    #endregion

    #region GetTimeUntilFull Tests

    [Fact]
    public void GetTimeUntilFull_WhenTheBucketIsMissingTokens_ReturnsTheTimeOfThePeriodsThatReturnThem()
    {
        var state = new TokenBucketState(1, NowMilliseconds);

        var timeUntilFull = state.GetTimeUntilFull(Limit);

        var expected = TimeSpan.FromSeconds(2);
        Assert.Equal(expected, timeUntilFull);
    }

    [Fact]
    public void GetTimeUntilFull_WhenTheBucketIsFull_ReturnsZero()
    {
        var state = new TokenBucketState(4, NowMilliseconds);

        var timeUntilFull = state.GetTimeUntilFull(Limit);

        Assert.Equal(TimeSpan.Zero, timeUntilFull);
    }

    #endregion
}
