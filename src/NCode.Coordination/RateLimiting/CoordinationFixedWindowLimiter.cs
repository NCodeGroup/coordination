// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;
using NCode.Coordination.Recipes;

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// A rate limiter that counts the permits of each fixed window in a counter on the coordination provider.
/// </summary>
/// <remarks>
/// A denied request still counts against its window, so a caller that keeps asking stays denied until the window ends.
/// </remarks>
/// <param name="counters">The counter operations the limiter is built on.</param>
/// <param name="time">The clock that dates each request.</param>
/// <param name="key">The key the limiter keeps its counters under.</param>
/// <param name="limit">The limit of the limiter.</param>
internal class CoordinationFixedWindowLimiter(
    ICounterService counters,
    TimeProvider time,
    CoordinationKey key,
    FixedWindowLimit limit
) : CoordinationRateLimiter(time, limit.PermitLimit)
{
    private ICounterService Counters { get; } = counters;

    private CoordinationKey Key { get; } = key;

    private FixedWindowLimit Limit { get; } = limit;

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquirePermitsAsync(
        int permitCount,
        CancellationToken cancellationToken
    )
    {
        var now = Time.GetUtcNow();
        var window = TimeWindow.Containing(now, Limit.Window);
        if (!window.TryCreateTimeToLive(now, SkewAllowance, out var timeToLive))
        {
            throw new InvalidOperationException("The window has already ended.");
        }

        var counterKey = RecipeKeys.Child(Key, window.Segment, nameof(key));
        var count = await Counters
            .IncrementAsync(counterKey, permitCount, timeToLive, null, cancellationToken)
            .ConfigureAwait(false);

        return count <= Limit.PermitLimit
            ? CoordinationRateLimitLease.Acquired
            : CoordinationRateLimitLease.Denied(window.End - now);
    }
}
