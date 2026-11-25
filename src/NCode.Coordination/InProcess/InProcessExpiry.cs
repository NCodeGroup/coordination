// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The end of a state entry's life, measured on the monotonic clock from the moment the entry was written.
/// </summary>
/// <param name="Start">The timestamp of the clock when the entry was written.</param>
/// <param name="Duration">How long the entry lives after <paramref name="Start"/>.</param>
internal readonly record struct InProcessExpiry(long Start, TimeSpan Duration)
{
    /// <summary>
    /// Starts an expiry now.
    /// </summary>
    /// <param name="time">The clock to measure on.</param>
    /// <param name="timeToLive">The time to live of the entry.</param>
    /// <returns>The expiry that ends after the time to live.</returns>
    public static InProcessExpiry Begin(TimeProvider time, TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(timeToLive);

        return new InProcessExpiry(time.GetTimestamp(), timeToLive.Duration);
    }

    /// <summary>
    /// Starts the expiry of a record's lifetime now, when the lifetime has a time to live.
    /// </summary>
    /// <param name="time">The clock to measure on.</param>
    /// <param name="lifetime">The lifetime of the record.</param>
    /// <returns>The expiry, or null when the lifetime has no time to live.</returns>
    public static InProcessExpiry? ForLifetime(TimeProvider time, RecordLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(lifetime);

        return lifetime.TimeToLive is { } timeToLive ? Begin(time, timeToLive) : null;
    }

    /// <summary>
    /// Determines whether the entry's life has ended.
    /// </summary>
    /// <param name="time">The clock the expiry was started on.</param>
    /// <returns>True when at least the duration has elapsed since the start.</returns>
    public bool HasEnded(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        return time.GetElapsedTime(Start) >= Duration;
    }
}
