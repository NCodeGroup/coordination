// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// One increment of a counter, with the limit its count may not exceed.
/// </summary>
public sealed class CounterIncrement
{
    /// <summary>
    /// Creates an increment.
    /// </summary>
    /// <param name="key">The key of the counter.</param>
    /// <param name="amount">The amount to add, at least 1.</param>
    /// <param name="limit">The count above which the increment is reported as exceeded, at least 0.</param>
    /// <param name="timeToLive">How long a counter this increment creates lasts.</param>
    /// <exception cref="ArgumentNullException">The key or the time to live is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The amount is below 1 or the limit is negative.</exception>
    public CounterIncrement(CoordinationKey key, long amount, long limit, TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeToLive);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        Key = key;
        Amount = amount;
        Limit = limit;
        TimeToLive = timeToLive;
    }

    /// <summary>
    /// Gets the key of the counter.
    /// </summary>
    public CoordinationKey Key { get; }

    /// <summary>
    /// Gets the amount to add.
    /// </summary>
    public long Amount { get; }

    /// <summary>
    /// Gets the count above which the increment is reported as exceeded.
    /// </summary>
    public long Limit { get; }

    /// <summary>
    /// Gets how long a counter this increment creates lasts.
    /// </summary>
    public TimeToLive TimeToLive { get; }
}
