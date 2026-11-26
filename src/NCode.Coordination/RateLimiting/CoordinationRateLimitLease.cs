// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;

namespace NCode.Coordination.RateLimiting;

/// <summary>
/// The lease that a coordination rate limiter returns: it holds no resource, and only reports whether the permits were
/// granted and, when they were not, how long to wait before trying again.
/// </summary>
internal sealed class CoordinationRateLimitLease : RateLimitLease
{
    /// <summary>
    /// Gets the lease of a request whose permits were granted.
    /// </summary>
    public static CoordinationRateLimitLease Acquired { get; } =
        new(isAcquired: true, retryAfter: null);

    /// <summary>
    /// Gets the lease of a request that was not granted and has no time to wait to report.
    /// </summary>
    public static CoordinationRateLimitLease NotAcquired { get; } =
        new(isAcquired: false, retryAfter: null);

    /// <summary>
    /// Initializes a lease.
    /// </summary>
    /// <param name="isAcquired">Whether the permits were granted.</param>
    /// <param name="retryAfter">How long to wait before trying again, or null when there is no time to report.</param>
    private CoordinationRateLimitLease(bool isAcquired, TimeSpan? retryAfter)
    {
        IsAcquired = isAcquired;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets how long to wait before trying again, or null when the lease reports no time.
    /// </summary>
    private TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Creates the lease of a request that was denied, which reports how long to wait before trying again.
    /// </summary>
    /// <param name="retryAfter">How long to wait before trying again; a negative time counts as zero.</param>
    /// <returns>The lease of the denied request.</returns>
    public static CoordinationRateLimitLease Denied(TimeSpan retryAfter) =>
        new(isAcquired: false, retryAfter: retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);

    /// <inheritdoc />
    public override bool IsAcquired { get; }

    /// <inheritdoc />
    public override IEnumerable<string> MetadataNames =>
        RetryAfter.HasValue ? [MetadataName.RetryAfter.Name] : [];

    /// <inheritdoc />
    public override bool TryGetMetadata(
        string metadataName,
        [NotNullWhen(true)] out object? metadata
    )
    {
        if (
            RetryAfter.HasValue
            && string.Equals(metadataName, MetadataName.RetryAfter.Name, StringComparison.Ordinal)
        )
        {
            metadata = RetryAfter.Value;
            return true;
        }

        metadata = null;
        return false;
    }
}
