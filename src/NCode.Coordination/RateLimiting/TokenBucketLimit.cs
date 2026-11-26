// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.RateLimiting;

namespace NCode.Coordination;

/// <summary>
/// The limit of a token-bucket rate limiter: how many tokens the bucket holds, and how many it refills each period.
/// </summary>
/// <remarks>
/// The replenishment period is a whole number of milliseconds above zero and at most 180 days, and the bucket refills
/// from empty to full within 180 days. The bucket refills in whole periods that align to the Unix epoch, so every node
/// with the same limit agrees on when a token returns.
/// </remarks>
public sealed class TokenBucketLimit
{
    /// <summary>
    /// Creates the limit of a token-bucket rate limiter.
    /// </summary>
    /// <param name="tokenLimit">How many tokens the bucket holds: at least 1.</param>
    /// <param name="tokensPerPeriod">How many tokens return each period: 1 to <paramref name="tokenLimit"/>.</param>
    /// <param name="replenishmentPeriod">
    /// The length of a period: a whole number of milliseconds above zero and at most 180 days, and short enough that the
    /// bucket refills from empty within 180 days.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The token limit is below 1, the tokens per period are outside 1 to the token limit, or the period is zero or less
    /// or longer than 180 days.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The period is not a whole number of milliseconds, or the bucket does not refill from empty within 180 days.
    /// </exception>
    public TokenBucketLimit(int tokenLimit, int tokensPerPeriod, TimeSpan replenishmentPeriod)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tokenLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokensPerPeriod, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tokensPerPeriod, tokenLimit);
        LimitWindow.Validate(replenishmentPeriod, nameof(replenishmentPeriod));

        var periodsToFull = ((long)tokenLimit + tokensPerPeriod - 1) / tokensPerPeriod;
        if (replenishmentPeriod.Ticks > LimitWindow.MaxLength.Ticks / periodsToFull)
        {
            throw new ArgumentException(
                "A token bucket refills from empty within 180 days.",
                nameof(replenishmentPeriod)
            );
        }

        TokenLimit = tokenLimit;
        TokensPerPeriod = tokensPerPeriod;
        ReplenishmentPeriod = replenishmentPeriod;
    }

    /// <summary>
    /// Gets how many tokens the bucket holds.
    /// </summary>
    public int TokenLimit { get; }

    /// <summary>
    /// Gets how many tokens return each period.
    /// </summary>
    public int TokensPerPeriod { get; }

    /// <summary>
    /// Gets the length of a period.
    /// </summary>
    public TimeSpan ReplenishmentPeriod { get; }
}
