// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// The base of the rate limiters that keep their counts on the coordination provider: it checks each request, tracks
/// when the limiter was last used and leaves the counting to the derived limiter.
/// </summary>
/// <remarks>
/// A limiter cannot reach the provider without waiting, so an attempt that must not wait is never acquired and the
/// middleware queues through <see cref="RateLimiter.AcquireAsync"/>. The limiter never queues a request itself.
/// </remarks>
/// <param name="time">The clock that dates each request and the limiter's idle time.</param>
/// <param name="permitLimit">The most permits one request may ask for.</param>
internal abstract class CoordinationRateLimiter(TimeProvider time, int permitLimit) : RateLimiter
{
    /// <summary>
    /// Gets the skew between the clocks of the nodes that the lifetime of each counter or record covers.
    /// </summary>
    protected static TimeSpan SkewAllowance { get; } = TimeSpan.FromMinutes(1);

    private long _lastUsed = time.GetTimestamp();
    private int _disposed;

    /// <summary>
    /// Gets the clock that dates each request.
    /// </summary>
    protected TimeProvider Time { get; } = time;

    private int PermitLimit { get; } = permitLimit;

    /// <inheritdoc />
    public override TimeSpan? IdleDuration => Time.GetElapsedTime(Volatile.Read(ref _lastUsed));

    /// <inheritdoc />
    public override RateLimiterStatistics? GetStatistics() => null;

    /// <inheritdoc />
    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        ThrowIfInvalid(permitCount);

        return CoordinationRateLimitLease.NotAcquired;
    }

    /// <inheritdoc />
    protected override ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount,
        CancellationToken cancellationToken
    )
    {
        ThrowIfInvalid(permitCount);

        Volatile.Write(ref _lastUsed, Time.GetTimestamp());

        return AcquirePermitsAsync(permitCount, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        Interlocked.Exchange(ref _disposed, 1);

        base.Dispose(disposing);
    }

    /// <summary>
    /// Counts a request's permits on the provider and decides whether they are granted.
    /// </summary>
    /// <param name="permitCount">The number of permits the request asks for, from 1 to the limit.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The lease that reports whether the permits were granted.</returns>
    protected abstract ValueTask<RateLimitLease> AcquirePermitsAsync(
        int permitCount,
        CancellationToken cancellationToken
    );

    private void ThrowIfInvalid(int permitCount)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(permitCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, PermitLimit);
    }
}
