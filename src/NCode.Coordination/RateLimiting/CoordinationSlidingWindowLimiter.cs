// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;
using NCode.Coordination.Recipes;

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// A rate limiter that counts the permits of each segment of a sliding window in a counter on the coordination
/// provider, and adds the counts of the segments before the current one.
/// </summary>
/// <remarks>
/// A denied request still counts against its segment, so a caller that keeps asking stays denied until the segment
/// slides out of the window.
/// </remarks>
/// <param name="counters">The counter operations the limiter is built on.</param>
/// <param name="time">The clock that dates each request.</param>
/// <param name="key">The key the limiter keeps its counters under.</param>
/// <param name="limit">The limit of the limiter.</param>
internal class CoordinationSlidingWindowLimiter(
    ICounterService counters,
    TimeProvider time,
    CoordinationKey key,
    SlidingWindowLimit limit
) : CoordinationRateLimiter(time, limit.PermitLimit)
{
    private ICounterService Counters { get; } = counters;

    private CoordinationKey Key { get; } = key;

    private SlidingWindowLimit Limit { get; } = limit;

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquirePermitsAsync(
        int permitCount,
        CancellationToken cancellationToken
    )
    {
        var now = Time.GetUtcNow();
        var current = TimeWindow.Containing(now, Limit.SegmentLength);
        if (
            !TimeToLive.TryCreateUntil(
                current.End + Limit.Window,
                now,
                SkewAllowance,
                out var timeToLive
            )
        )
        {
            throw new InvalidOperationException("The window has already ended.");
        }

        var previous = 0L;
        for (var offset = 1; offset < Limit.SegmentsPerWindow; offset++)
        {
            var start = current.Start - (offset * Limit.SegmentLength);
            if (start < DateTimeOffset.UnixEpoch)
            {
                break;
            }

            var segment = new TimeWindow(start, Limit.SegmentLength);
            var segmentKey = RecipeKeys.Child(Key, segment.Segment, nameof(key));
            previous += await Counters
                .ReadAsync(segmentKey, null, cancellationToken)
                .ConfigureAwait(false);
        }

        var counterKey = RecipeKeys.Child(Key, current.Segment, nameof(key));
        var count = await Counters
            .IncrementAsync(counterKey, permitCount, timeToLive, null, cancellationToken)
            .ConfigureAwait(false);

        return previous + count <= Limit.PermitLimit
            ? CoordinationRateLimitLease.Acquired
            : CoordinationRateLimitLease.Denied(current.End - now);
    }
}
