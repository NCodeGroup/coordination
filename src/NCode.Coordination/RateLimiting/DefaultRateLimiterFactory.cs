// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;
using NCode.Coordination.Recipes;

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// Creates rate limiters over the counter and record operations.
/// </summary>
/// <param name="counters">The counter operations the window limiters are built on.</param>
/// <param name="records">The record operations the token bucket limiters are built on.</param>
/// <param name="time">The clock that dates each request.</param>
internal class DefaultRateLimiterFactory(
    ICounterService counters,
    IRecordService records,
    TimeProvider time
) : IRateLimiterFactory
{
    /// <summary>
    /// Gets the counter operations that the window limiters are built on.
    /// </summary>
    private ICounterService Counters { get; } = counters;

    /// <summary>
    /// Gets the record operations that the token bucket limiters are built on.
    /// </summary>
    private IRecordService Records { get; } = records;

    /// <summary>
    /// Gets the clock that dates each request.
    /// </summary>
    private TimeProvider Time { get; } = time;

    /// <inheritdoc />
    public RateLimiter CreateFixedWindowLimiter(CoordinationKey key, FixedWindowLimit limit)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(limit);
        RecipeKeys.ThrowIfFull(key, nameof(key));

        return new CoordinationFixedWindowLimiter(Counters, Time, key, limit);
    }

    /// <inheritdoc />
    public RateLimiter CreateSlidingWindowLimiter(CoordinationKey key, SlidingWindowLimit limit)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(limit);
        RecipeKeys.ThrowIfFull(key, nameof(key));

        return new CoordinationSlidingWindowLimiter(Counters, Time, key, limit);
    }

    /// <inheritdoc />
    public RateLimiter CreateTokenBucketLimiter(CoordinationKey key, TokenBucketLimit limit)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(limit);

        return new CoordinationTokenBucketLimiter(Records, Time, key, limit);
    }
}
