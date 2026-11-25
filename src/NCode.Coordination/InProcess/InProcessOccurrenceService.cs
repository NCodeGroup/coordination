// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The occurrences of the in-process provider.
/// </summary>
/// <param name="time">The clock the occurrences' expiries are measured on.</param>
/// <remarks>
/// Every operation runs entirely while holding the service's lock, so it is linearizable in the process.
/// </remarks>
internal class InProcessOccurrenceService(TimeProvider time) : IOccurrenceService
{
    /// <summary>
    /// Gets the lock that every operation holds.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the clock the occurrences' expiries are measured on.
    /// </summary>
    private TimeProvider Time { get; } = time;

    /// <summary>
    /// Gets the recorded keys, each held until its time to live ends.
    /// </summary>
    private InProcessExpiringTable<CoordinationKey, bool> Occurrences { get; } = new(time);

    /// <inheritdoc />
    public ValueTask<OccurrenceStatus> TryRecordAsync(
        CoordinationKey key,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeToLive);
        InProcessGuard.Check(options, cancellationToken);

        lock (Gate)
        {
            if (Occurrences.TryGet(key, out _))
            {
                return ValueTask.FromResult(OccurrenceStatus.Duplicate);
            }

            var expiry = InProcessExpiry.Begin(Time, timeToLive);
            Occurrences.Set(key, true, expiry);
            return ValueTask.FromResult(OccurrenceStatus.Recorded);
        }
    }
}
