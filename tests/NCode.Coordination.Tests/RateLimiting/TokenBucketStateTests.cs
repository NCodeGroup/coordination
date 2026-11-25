// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class TokenBucketStateTests
{
    private const long S = 1767225600000;

    private static TokenBucketLimit Limit { get; } = new(4, 2, TimeSpan.FromSeconds(1));

    #region Encode and Decode Tests

    [Fact]
    public void Encode_WhenTheStateHasValues_WritesTwoBigEndianIntegers()
    {
        var state = new TokenBucketState(3, S);

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

    [Fact]
    public void Decode_WhenTheValueWasEncoded_ReturnsTheState()
    {
        var state = new TokenBucketState(3, S);

        var decoded = TokenBucketState.Decode(state.Encode());

        Assert.Equal(state, decoded);
    }

    [Fact]
    public void Decode_WhenTheValueIsShort_Throws()
    {
        var value = new byte[TokenBucketState.EncodedLength - 1];

        Assert.Throws<InvalidDataException>(() => TokenBucketState.Decode(value));
    }

    [Fact]
    public void Decode_WhenTheTokensAreNegative_Throws()
    {
        var value = new TokenBucketState(-1, S).Encode();

        Assert.Throws<InvalidDataException>(() => TokenBucketState.Decode(value));
    }

    #endregion

    #region Replenish Tests

    [Fact]
    public void Replenish_WhenOnePeriodAndAHalfHavePassed_ReturnsOnePeriodOfTokens()
    {
        var state = new TokenBucketState(0, S);

        var replenished = state.Replenish(Limit, S + 1500);

        Assert.Equal(new TokenBucketState(2, S + 1000), replenished);
    }

    [Fact]
    public void Replenish_WhenEnoughPeriodsHavePassedToFill_ReturnsAFullBucketAtTheInstant()
    {
        var state = new TokenBucketState(0, S);

        var replenished = state.Replenish(Limit, S + 2500);

        Assert.Equal(new TokenBucketState(4, S + 2500), replenished);
    }

    [Fact]
    public void Replenish_WhenNoWholePeriodHasPassed_ReturnsTheState()
    {
        var state = new TokenBucketState(1, S);

        var replenished = state.Replenish(Limit, S + 999);

        Assert.Equal(state, replenished);
    }

    [Fact]
    public void Replenish_WhenTheBucketIsFull_ReturnsAFullBucketAtTheInstant()
    {
        var state = new TokenBucketState(4, S);

        var replenished = state.Replenish(Limit, S + 5000);

        Assert.Equal(new TokenBucketState(4, S + 5000), replenished);
    }

    #endregion

    #region GetRetryAfter Tests

    [Fact]
    public void GetRetryAfter_WhenTheBucketHoldsTooFewTokens_ReturnsTheTimeUntilThePeriodThatReturnsEnough()
    {
        var state = new TokenBucketState(1, S);

        var retryAfter = state.GetRetryAfter(Limit, 4, S + 200);

        Assert.Equal(TimeSpan.FromMilliseconds(1800), retryAfter);
    }

    [Fact]
    public void GetRetryAfter_WhenTheBucketHoldsEnoughTokens_ReturnsZero()
    {
        var state = new TokenBucketState(3, S);

        var retryAfter = state.GetRetryAfter(Limit, 3, S);

        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    #endregion

    #region GetTimeUntilFull Tests

    [Fact]
    public void GetTimeUntilFull_WhenTheBucketIsMissingTokens_ReturnsTheTimeOfThePeriodsThatReturnThem()
    {
        var state = new TokenBucketState(1, S);

        var timeUntilFull = state.GetTimeUntilFull(Limit);

        Assert.Equal(TimeSpan.FromSeconds(2), timeUntilFull);
    }

    [Fact]
    public void GetTimeUntilFull_WhenTheBucketIsFull_ReturnsZero()
    {
        var state = new TokenBucketState(4, S);

        var timeUntilFull = state.GetTimeUntilFull(Limit);

        Assert.Equal(TimeSpan.Zero, timeUntilFull);
    }

    #endregion
}
