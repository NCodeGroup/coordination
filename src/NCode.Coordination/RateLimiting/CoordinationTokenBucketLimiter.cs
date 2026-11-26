// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// A rate limiter that keeps the state of a token bucket in one record on the coordination provider and takes each
/// request's tokens with a compare-and-swap.
/// </summary>
/// <remarks>
/// A request that finds too few tokens is denied without a write, so only the requests that are granted change the
/// bucket. An absent record is a full bucket, and the record lives until the bucket would be full again.
/// </remarks>
/// <param name="records">The record operations the limiter is built on.</param>
/// <param name="time">The clock that dates each request and replenishes the bucket.</param>
/// <param name="key">The key of the record that holds the bucket.</param>
/// <param name="limit">The limit of the limiter.</param>
internal class CoordinationTokenBucketLimiter(
    IRecordService records,
    TimeProvider time,
    CoordinationKey key,
    TokenBucketLimit limit
) : CoordinationRateLimiter(time, limit.TokenLimit)
{
    /// <summary>
    /// Gets the record operations that read and write the record that holds the bucket.
    /// </summary>
    private IRecordService Records { get; } = records;

    /// <summary>
    /// Gets the key of the record that holds the bucket.
    /// </summary>
    private CoordinationKey Key { get; } = key;

    /// <summary>
    /// Gets the limit of the limiter.
    /// </summary>
    private TokenBucketLimit Limit { get; } = limit;

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquirePermitsAsync(
        int permitCount,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nowMs = Time.GetUtcNow().ToUnixTimeMilliseconds();
            var stored = await Records
                .ReadAsync(Key, null, cancellationToken)
                .ConfigureAwait(false);

            TokenBucketState state;
            if (stored is null)
            {
                state = TokenBucketState.Full(Limit, nowMs);
            }
            else
            {
                var decoded = TokenBucketState.Decode(stored.Value.Span);
                state = decoded.Replenish(Limit, nowMs);
            }

            if (state.Tokens < permitCount)
            {
                var retryAfter = state.GetRetryAfter(Limit, permitCount, nowMs);
                return CoordinationRateLimitLease.Denied(retryAfter);
            }

            var next = state with { Tokens = state.Tokens - permitCount };
            var timeUntilFull = next.GetTimeUntilFull(Limit) + SkewAllowance;
            var timeToLive = new TimeToLive(timeUntilFull);
            var lifetime = RecordLifetime.ForTimeToLive(timeToLive);
            var encoded = next.Encode();

            RecordWriteResult written;
            if (stored is null)
            {
                written = await Records
                    .CreateAsync(Key, encoded, lifetime, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                written = await Records
                    .CompareAndSwapAsync(
                        Key,
                        stored.Revision,
                        encoded,
                        lifetime,
                        null,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (written.IsWritten)
            {
                return CoordinationRateLimitLease.Acquired;
            }
        }
    }
}
