// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// How long a stored state lasts: a duration above zero and at most <see cref="MaxDuration"/>.
/// </summary>
/// <remarks>
/// The provider measures the duration from when its server applies the write, and rounds it up to the precision the
/// server keeps, never down, so a state never ends before the duration has passed. Create one from an instant on the
/// caller's clock with <see cref="TryCreateUntil"/>.
/// </remarks>
public sealed record TimeToLive
{
    /// <summary>
    /// Gets the longest time to live, 366 days.
    /// </summary>
    public static TimeSpan MaxDuration { get; } = TimeSpan.FromDays(366);

    /// <summary>
    /// Creates a time to live.
    /// </summary>
    /// <param name="duration">The duration: above zero and at most <see cref="MaxDuration"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The duration is zero or less, or longer than <see cref="MaxDuration"/>.
    /// </exception>
    public TimeToLive(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duration, MaxDuration);

        Duration = duration;
    }

    /// <summary>
    /// Gets how long the state lasts, measured from when the provider's server applies the write.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Creates the time to live that ends at an instant on the caller's clock.
    /// </summary>
    /// <remarks>
    /// The duration is the time from <paramref name="now"/> to <paramref name="instant"/>, lengthened by
    /// <paramref name="skewAllowance"/> to cover the skew between the clocks of the nodes. A false result means the
    /// instant, allowance included, has passed, so every node already refuses what the state would guard.
    /// </remarks>
    /// <param name="instant">The instant the state ends, on the caller's clock.</param>
    /// <param name="now">The current instant on the caller's clock.</param>
    /// <param name="skewAllowance">
    /// The skew between nodes the duration covers: zero to <see cref="MaxDuration"/>.
    /// </param>
    /// <param name="timeToLive">The time to live when the result is true, and null otherwise.</param>
    /// <returns>True when the instant, allowance included, lies after <paramref name="now"/>; otherwise false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The allowance is negative or longer than <see cref="MaxDuration"/>, or the duration is longer than
    /// <see cref="MaxDuration"/>.
    /// </exception>
    public static bool TryCreateUntil(
        DateTimeOffset instant,
        DateTimeOffset now,
        TimeSpan skewAllowance,
        [NotNullWhen(true)] out TimeToLive? timeToLive
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(skewAllowance, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(skewAllowance, MaxDuration);

        var remaining = instant - now + skewAllowance;

        if (remaining <= TimeSpan.Zero)
        {
            timeToLive = null;
            return false;
        }

        if (remaining > MaxDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instant),
                instant,
                "The instant lies beyond the longest time to live."
            );
        }

        timeToLive = new TimeToLive(remaining);
        return true;
    }

    /// <summary>
    /// Returns the duration in the invariant "c" format, such as <c>00:05:00</c>.
    /// </summary>
    /// <returns>The duration as text.</returns>
    public override string ToString() => Duration.ToString("c", CultureInfo.InvariantCulture);
}
