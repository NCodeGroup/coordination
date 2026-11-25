// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The moment a holder treats its session, lease, semaphore slot, leadership or claim as lost, on its own monotonic
/// clock.
/// </summary>
/// <remarks>
/// Begin it before sending the request that grants or renews the ownership, with the time to live the request asks
/// for. It passes the drift allowance before that time to live ends. A pause can outlast it, so a resource that takes
/// writes from holders compares fencing tokens.
/// </remarks>
public sealed class HolderDeadline
{
    private HolderDeadline(TimeProvider time, long start, TimeSpan lifetime)
    {
        Time = time;
        Start = start;
        Lifetime = lifetime;
    }

    private TimeProvider Time { get; }

    private long Start { get; }

    /// <summary>
    /// Gets how long the deadline lasts from when it began: the time to live less the drift allowance.
    /// </summary>
    public TimeSpan Lifetime { get; }

    /// <summary>
    /// Gets the time left before the deadline passes, or zero once it has passed.
    /// </summary>
    public TimeSpan Remaining
    {
        get
        {
            var remaining = Lifetime - Time.GetElapsedTime(Start);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Gets a value that indicates whether the deadline has passed.
    /// </summary>
    public bool HasPassed => Time.GetElapsedTime(Start) >= Lifetime;

    /// <summary>
    /// Begins a deadline at the current instant on the holder's clock.
    /// </summary>
    /// <param name="time">The holder's clock, read on its monotonic timestamp.</param>
    /// <param name="timeToLive">The time to live the request asks for, never longer than the one granted.</param>
    /// <param name="driftAllowance">
    /// The margin for drift that the deadline passes before the time to live ends: zero up to, not including, the
    /// time to live.
    /// </param>
    /// <returns>The deadline, which passes after the time to live less the drift allowance.</returns>
    /// <exception cref="ArgumentNullException">The clock or the time to live is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The drift allowance is negative, or is not shorter than the time to live.
    /// </exception>
    public static HolderDeadline Begin(
        TimeProvider time,
        TimeToLive timeToLive,
        TimeSpan driftAllowance
    )
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(timeToLive);
        ArgumentOutOfRangeException.ThrowIfLessThan(driftAllowance, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(driftAllowance, timeToLive.Duration);

        return new HolderDeadline(time, time.GetTimestamp(), timeToLive.Duration - driftAllowance);
    }
}
