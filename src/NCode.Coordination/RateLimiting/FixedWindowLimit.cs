// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.RateLimiting;

namespace NCode.Coordination;

/// <summary>
/// The limit of a fixed-window rate limiter: how many permits each window allows.
/// </summary>
/// <remarks>
/// The window is a whole number of milliseconds above zero and at most 180 days. Windows align to the Unix epoch, so
/// every node with the same window length agrees on where each window starts and ends.
/// </remarks>
public sealed class FixedWindowLimit
{
    /// <summary>
    /// Creates the limit of a fixed-window rate limiter.
    /// </summary>
    /// <param name="permitLimit">How many permits each window allows: at least 1.</param>
    /// <param name="window">The length of a window: a whole number of milliseconds above zero and at most 180 days.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The permit limit is below 1, or the window is zero or less or longer than 180 days.
    /// </exception>
    /// <exception cref="ArgumentException">The window is not a whole number of milliseconds.</exception>
    public FixedWindowLimit(int permitLimit, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        LimitWindow.Validate(window, nameof(window));

        PermitLimit = permitLimit;
        Window = window;
    }

    /// <summary>
    /// Gets how many permits each window allows.
    /// </summary>
    public int PermitLimit { get; }

    /// <summary>
    /// Gets the length of a window.
    /// </summary>
    public TimeSpan Window { get; }
}
