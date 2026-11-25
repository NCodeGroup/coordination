// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// A span of time aligned to a multiple of its length from the Unix epoch, which claims and counters name by its
/// <see cref="Segment"/>.
/// </summary>
/// <remarks>
/// The window containing an instant is computed on the caller's clock, in UTC and whole milliseconds, so every node
/// with the same length agrees on the windows. Create the window that holds an instant with
/// <see cref="Containing"/>.
/// </remarks>
public sealed record TimeWindow
{
    /// <summary>
    /// Creates a time window.
    /// </summary>
    /// <param name="start">The start: a UTC instant at or after the Unix epoch, and a multiple of the length from it.</param>
    /// <param name="length">
    /// The length: a whole number of milliseconds above zero and at most <see cref="TimeToLive.MaxDuration"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The length is zero or less or above the longest time to live, or the start lies before the Unix epoch or too
    /// close to the largest instant.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The length is not a whole number of milliseconds, or the start is not a UTC instant or not a multiple of the
    /// length from the Unix epoch.
    /// </exception>
    public TimeWindow(DateTimeOffset start, TimeSpan length)
    {
        ValidateLength(length);

        if (start.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A window starts at a UTC instant.", nameof(start));
        }

        if (start < DateTimeOffset.UnixEpoch || start > DateTimeOffset.MaxValue - length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A window starts at or after the Unix epoch and ends before the largest instant."
            );
        }

        if ((start - DateTimeOffset.UnixEpoch).Ticks % length.Ticks != 0)
        {
            throw new ArgumentException(
                "A window starts at a multiple of its length from the Unix epoch.",
                nameof(start)
            );
        }

        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets the instant the window starts, in UTC.
    /// </summary>
    public DateTimeOffset Start { get; }

    /// <summary>
    /// Gets the length of the window.
    /// </summary>
    public TimeSpan Length { get; }

    /// <summary>
    /// Gets the instant the window ends, which is the start of the next window.
    /// </summary>
    public DateTimeOffset End => Start + Length;

    /// <summary>
    /// Gets the key segment that names the window: its start in Unix milliseconds.
    /// </summary>
    public KeySegment Segment => KeySegment.FromInt64(Start.ToUnixTimeMilliseconds());

    /// <summary>
    /// Creates the window of a length that contains an instant.
    /// </summary>
    /// <param name="instant">The instant on the caller's clock, at or after the Unix epoch.</param>
    /// <param name="length">
    /// The length: a whole number of milliseconds above zero and at most <see cref="TimeToLive.MaxDuration"/>.
    /// </param>
    /// <returns>The window that starts at or before the instant and ends after it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The length is zero or less or above the longest time to live, or the instant lies before the Unix epoch.
    /// </exception>
    /// <exception cref="ArgumentException">The length is not a whole number of milliseconds.</exception>
    public static TimeWindow Containing(DateTimeOffset instant, TimeSpan length)
    {
        ValidateLength(length);
        ArgumentOutOfRangeException.ThrowIfLessThan(instant, DateTimeOffset.UnixEpoch);

        var elapsed = (instant - DateTimeOffset.UnixEpoch).Ticks;
        var start = DateTimeOffset.UnixEpoch.AddTicks(elapsed - (elapsed % length.Ticks));

        return new(start, length);
    }

    /// <summary>
    /// Creates the time to live that ends the window's state at its end, lengthened by a skew allowance.
    /// </summary>
    /// <param name="now">The current instant on the caller's clock.</param>
    /// <param name="skewAllowance">The skew between nodes the duration covers: zero to the longest time to live.</param>
    /// <param name="timeToLive">The time to live when the result is true, and null otherwise.</param>
    /// <returns>True when the window's end, allowance included, lies after <paramref name="now"/>; otherwise false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The allowance is negative or too long, or the duration is longer than the longest time to live.
    /// </exception>
    public bool TryCreateTimeToLive(
        DateTimeOffset now,
        TimeSpan skewAllowance,
        [NotNullWhen(true)] out TimeToLive? timeToLive
    ) => TimeToLive.TryCreateUntil(End, now, skewAllowance, out timeToLive);

    /// <summary>
    /// Returns the start in the invariant "O" format, a slash and the length in the invariant "c" format.
    /// </summary>
    /// <returns>The window as text.</returns>
    public override string ToString() =>
        $"{Start.ToString("O", CultureInfo.InvariantCulture)}/{Length.ToString("c", CultureInfo.InvariantCulture)}";

    private static void ValidateLength(TimeSpan length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, TimeToLive.MaxDuration);

        if (length.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentException(
                "A window's length is a whole number of milliseconds.",
                nameof(length)
            );
        }
    }
}
