// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// Checks the window or period of a rate limit.
/// </summary>
internal static class LimitWindow
{
    /// <summary>
    /// Gets the longest window or period a rate limit has.
    /// </summary>
    public static TimeSpan MaxLength { get; } = TimeSpan.FromDays(180);

    /// <summary>
    /// Checks the length of a window or period.
    /// </summary>
    /// <param name="length">The length: above zero, at most <see cref="MaxLength"/>, and a whole number of milliseconds.</param>
    /// <param name="paramName">The name of the parameter that holds the length.</param>
    /// <exception cref="ArgumentOutOfRangeException">The length is zero or less or above the longest length.</exception>
    /// <exception cref="ArgumentException">The length is not a whole number of milliseconds.</exception>
    public static void Validate(TimeSpan length, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength, paramName);

        if (length.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentException(
                "A rate limit's window or period is a whole number of milliseconds.",
                paramName
            );
        }
    }
}
