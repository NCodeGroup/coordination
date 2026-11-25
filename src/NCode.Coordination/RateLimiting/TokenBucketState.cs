// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// The state of a token bucket as a limiter stores it: how many tokens the bucket holds and the instant, in Unix
/// milliseconds, up to which it has been replenished.
/// </summary>
/// <param name="Tokens">The number of tokens in the bucket.</param>
/// <param name="ReplenishedAt">The Unix time in milliseconds up to which the bucket has been replenished.</param>
internal readonly record struct TokenBucketState(long Tokens, long ReplenishedAt)
{
    /// <summary>
    /// The length in bytes of an encoded state: the tokens and the replenishment time as two big-endian integers.
    /// </summary>
    public const int EncodedLength = 16;

    /// <summary>
    /// Creates the state of a full bucket at an instant.
    /// </summary>
    /// <param name="limit">The limit of the bucket.</param>
    /// <param name="now">The Unix time in milliseconds.</param>
    /// <returns>The state of a bucket holding as many tokens as its limit.</returns>
    public static TokenBucketState Full(TokenBucketLimit limit, long now) =>
        new(limit.TokenLimit, now);

    /// <summary>
    /// Reads a state from its encoding.
    /// </summary>
    /// <param name="value">The 16 bytes that <see cref="Encode"/> wrote.</param>
    /// <returns>The state that the bytes hold.</returns>
    /// <exception cref="InvalidDataException">The value is not 16 bytes, or holds a negative number.</exception>
    public static TokenBucketState Decode(ReadOnlySpan<byte> value)
    {
        if (value.Length != EncodedLength)
        {
            throw new InvalidDataException($"A token bucket's state is {EncodedLength} bytes.");
        }

        var tokens = BinaryPrimitives.ReadInt64BigEndian(value);
        var replenishedAt = BinaryPrimitives.ReadInt64BigEndian(value[sizeof(long)..]);
        if (tokens < 0 || replenishedAt < 0)
        {
            throw new InvalidDataException("A token bucket's state holds no negative number.");
        }

        return new TokenBucketState(tokens, replenishedAt);
    }

    /// <summary>
    /// Writes the state as 16 bytes.
    /// </summary>
    /// <returns>The tokens and then the replenishment time, each a big-endian 64-bit integer.</returns>
    public byte[] Encode()
    {
        var value = new byte[EncodedLength];
        BinaryPrimitives.WriteInt64BigEndian(value, Tokens);
        BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(sizeof(long)), ReplenishedAt);
        return value;
    }

    /// <summary>
    /// Adds the tokens that whole periods have returned by an instant.
    /// </summary>
    /// <param name="limit">The limit of the bucket.</param>
    /// <param name="now">The Unix time in milliseconds.</param>
    /// <returns>
    /// The state after the whole periods that have passed since the last replenishment, capped at the limit; a full
    /// bucket is replenished up to the instant.
    /// </returns>
    public TokenBucketState Replenish(TokenBucketLimit limit, long now)
    {
        if (Tokens >= limit.TokenLimit)
        {
            return new TokenBucketState(limit.TokenLimit, Math.Max(ReplenishedAt, now));
        }

        if (now <= ReplenishedAt)
        {
            return this;
        }

        var period = GetPeriodMilliseconds(limit);
        var periods = (now - ReplenishedAt) / period;
        if (periods >= GetPeriodsToFull(limit))
        {
            return new TokenBucketState(limit.TokenLimit, now);
        }

        return new TokenBucketState(
            Tokens + periods * limit.TokensPerPeriod,
            ReplenishedAt + periods * period
        );
    }

    /// <summary>
    /// Gets how long until the bucket holds enough tokens for a request.
    /// </summary>
    /// <param name="limit">The limit of the bucket.</param>
    /// <param name="permitCount">The number of tokens the request needs.</param>
    /// <param name="now">The Unix time in milliseconds.</param>
    /// <returns>Zero when the bucket holds enough tokens, otherwise the time until the next period that returns enough.</returns>
    public TimeSpan GetRetryAfter(TokenBucketLimit limit, int permitCount, long now)
    {
        if (Tokens >= permitCount)
        {
            return TimeSpan.Zero;
        }

        var missing = permitCount - Tokens;
        var periods = (missing + limit.TokensPerPeriod - 1) / limit.TokensPerPeriod;
        var milliseconds = ReplenishedAt + periods * GetPeriodMilliseconds(limit) - now;
        return TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
    }

    /// <summary>
    /// Gets how long the bucket takes to refill completely.
    /// </summary>
    /// <param name="limit">The limit of the bucket.</param>
    /// <returns>Zero when the bucket is full, otherwise the time of the periods that return the missing tokens.</returns>
    public TimeSpan GetTimeUntilFull(TokenBucketLimit limit)
    {
        var milliseconds = GetPeriodsToFull(limit) * GetPeriodMilliseconds(limit);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private long GetPeriodsToFull(TokenBucketLimit limit)
    {
        var missing = limit.TokenLimit - Tokens;
        return missing <= 0 ? 0 : (missing + limit.TokensPerPeriod - 1) / limit.TokensPerPeriod;
    }

    private static long GetPeriodMilliseconds(TokenBucketLimit limit) =>
        limit.ReplenishmentPeriod.Ticks / TimeSpan.TicksPerMillisecond;
}
