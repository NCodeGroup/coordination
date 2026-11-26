// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.RateLimiting;

namespace NCode.Coordination;

/// <summary>
/// The limit of a sliding-window rate limiter: how many permits the window allows, and how many segments it divides into.
/// </summary>
/// <remarks>
/// The window is a whole number of milliseconds above zero and at most 180 days, and it divides into segments of whole
/// milliseconds. Segments align to the Unix epoch, so every node with the same window length and segment count agrees
/// on where each segment starts and ends. Each request reads the counter of every earlier segment in the window and
/// increments the current one's, so it makes as many calls to the provider as the window has segments.
/// </remarks>
public sealed class SlidingWindowLimit
{
    /// <summary>
    /// The most segments a window divides into.
    /// </summary>
    public const int MaxSegmentsPerWindow = 16;

    /// <summary>
    /// Creates the limit of a sliding-window rate limiter.
    /// </summary>
    /// <param name="permitLimit">How many permits the window allows: at least 1.</param>
    /// <param name="window">The length of the window: a whole number of milliseconds above zero and at most 180 days.</param>
    /// <param name="segmentsPerWindow">How many segments the window divides into: 1 to <see cref="MaxSegmentsPerWindow"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The permit limit is below 1, the window is zero or less or longer than 180 days, or the segment count is outside
    /// 1 to <see cref="MaxSegmentsPerWindow"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The window is not a whole number of milliseconds, or does not divide into segments of whole milliseconds.
    /// </exception>
    public SlidingWindowLimit(int permitLimit, TimeSpan window, int segmentsPerWindow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        LimitWindow.Validate(window, nameof(window));
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentsPerWindow, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(segmentsPerWindow, MaxSegmentsPerWindow);

        if (window.Ticks % (segmentsPerWindow * TimeSpan.TicksPerMillisecond) != 0)
        {
            throw new ArgumentException(
                "A sliding window divides into segments of whole milliseconds.",
                nameof(segmentsPerWindow)
            );
        }

        PermitLimit = permitLimit;
        Window = window;
        SegmentsPerWindow = segmentsPerWindow;
    }

    /// <summary>
    /// Gets how many permits the window allows.
    /// </summary>
    public int PermitLimit { get; }

    /// <summary>
    /// Gets the length of the window.
    /// </summary>
    public TimeSpan Window { get; }

    /// <summary>
    /// Gets how many segments the window divides into.
    /// </summary>
    public int SegmentsPerWindow { get; }

    /// <summary>
    /// Gets the length of one segment.
    /// </summary>
    public TimeSpan SegmentLength => Window / SegmentsPerWindow;
}
