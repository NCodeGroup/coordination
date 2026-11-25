// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;

namespace NCode.Coordination;

/// <summary>
/// Creates rate limiters whose counts live on the coordination provider, so every node shares them.
/// </summary>
/// <remarks>
/// Limiters created on one key share their count across nodes, so a host passes them to the rate limiting middleware
/// of ASP.NET Core through <c>RateLimitPartition.Get</c>. <c>AttemptAcquire</c> answers not acquired, since it cannot
/// reach the provider without blocking. <c>AcquireAsync</c> never queues, gives
/// <see cref="MetadataName.RetryAfter"/> when it denies a request, and throws when the provider fails. Use one kind
/// of limit per key.
/// </remarks>
public interface IRateLimiterFactory
{
    /// <summary>
    /// Creates a limiter that allows a number of permits in each fixed window.
    /// </summary>
    /// <remarks>
    /// The key has at most 15 segments, since the limiter keeps one counter under it for each window.
    /// </remarks>
    /// <param name="key">The key the limiter keeps its counters under.</param>
    /// <param name="limit">The limit of the limiter.</param>
    /// <returns>The limiter, which the caller disposes.</returns>
    RateLimiter CreateFixedWindowLimiter(CoordinationKey key, FixedWindowLimit limit);

    /// <summary>
    /// Creates a limiter that allows a number of permits in a window that slides by segments.
    /// </summary>
    /// <remarks>
    /// The key has at most 15 segments, since the limiter keeps one counter under it for each segment.
    /// </remarks>
    /// <param name="key">The key the limiter keeps its counters under.</param>
    /// <param name="limit">The limit of the limiter.</param>
    /// <returns>The limiter, which the caller disposes.</returns>
    RateLimiter CreateSlidingWindowLimiter(CoordinationKey key, SlidingWindowLimit limit);

    /// <summary>
    /// Creates a limiter that takes permits from a bucket of tokens that refills over time.
    /// </summary>
    /// <remarks>
    /// The limiter keeps the bucket in one record at the key.
    /// </remarks>
    /// <param name="key">The key of the record that holds the bucket.</param>
    /// <param name="limit">The limit of the limiter.</param>
    /// <returns>The limiter, which the caller disposes.</returns>
    RateLimiter CreateTokenBucketLimiter(CoordinationKey key, TokenBucketLimit limit);
}
