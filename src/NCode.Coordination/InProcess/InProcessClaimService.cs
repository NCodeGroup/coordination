// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The claims of the in-process provider.
/// </summary>
/// <param name="time">The clock the claims' expiries are measured on.</param>
/// <remarks>
/// Every operation runs entirely while holding the service's lock, so it is linearizable in the process.
/// </remarks>
internal class InProcessClaimService(TimeProvider time) : IClaimService
{
    /// <summary>
    /// Gets the lock that every operation holds.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the clock the claims' expiries are measured on.
    /// </summary>
    private TimeProvider Time { get; } = time;

    /// <summary>
    /// Gets the claimed keys, each held until its time to live ends.
    /// </summary>
    private InProcessExpiringTable<CoordinationKey, bool> Claims { get; } = new(time);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// The name has as many segments as a key can hold, so the window's segment does not fit after them.
    /// </exception>
    public ValueTask<ClaimStatus> TryClaimAsync(
        CoordinationKey name,
        TimeWindow window,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(timeToLive);

        if (name.Segments.Length >= CoordinationKey.MaxSegments)
        {
            throw new ArgumentException(
                $"A claim's name has at most {CoordinationKey.MaxSegments - 1} segments, so the window's segment fits after them.",
                nameof(name)
            );
        }

        InProcessGuard.Check(options, cancellationToken);

        var key = new CoordinationKey(name.Keyspace, [.. name.Segments, window.Segment])
        {
            PartitionDepth = name.PartitionDepth,
        };

        using var scope = Gate.EnterScope();

        if (Claims.TryGet(key, out _))
        {
            return ValueTask.FromResult(ClaimStatus.Taken);
        }

        var expiry = InProcessExpiry.Begin(Time, timeToLive);
        Claims.Set(key, true, expiry);

        return ValueTask.FromResult(ClaimStatus.Claimed);
    }
}
