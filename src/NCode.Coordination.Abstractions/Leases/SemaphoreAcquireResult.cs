// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination;

/// <summary>
/// The result of trying to acquire a semaphore slot: acquired with its grant, full, a capacity conflict, or the
/// session ended.
/// </summary>
/// <remarks>
/// Create an acquired result with <see cref="Acquired(SemaphoreGrant)"/>; <see cref="Full"/>,
/// <see cref="CapacityConflict"/> and <see cref="SessionEnded"/> are shared.
/// </remarks>
public sealed class SemaphoreAcquireResult
{
    /// <summary>
    /// Gets the result for a semaphore whose slots are all held.
    /// </summary>
    public static SemaphoreAcquireResult Full { get; } = new(SemaphoreAcquireStatus.Full, null);

    /// <summary>
    /// Gets the result for a semaphore whose holders use another capacity.
    /// </summary>
    public static SemaphoreAcquireResult CapacityConflict { get; } =
        new(SemaphoreAcquireStatus.CapacityConflict, null);

    /// <summary>
    /// Gets the result for a session that has ended.
    /// </summary>
    public static SemaphoreAcquireResult SessionEnded { get; } =
        new(SemaphoreAcquireStatus.SessionEnded, null);

    private SemaphoreAcquireResult(SemaphoreAcquireStatus status, SemaphoreGrant? grant)
    {
        Status = status;
        Grant = grant;
    }

    /// <summary>
    /// Gets the outcome of the attempt.
    /// </summary>
    public SemaphoreAcquireStatus Status { get; }

    /// <summary>
    /// Gets the grant of the acquired slot, or null when no slot was acquired.
    /// </summary>
    public SemaphoreGrant? Grant { get; }

    /// <summary>
    /// Gets a value indicating whether a slot was acquired.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Grant))]
    public bool IsAcquired => Status == SemaphoreAcquireStatus.Acquired;

    /// <summary>
    /// Creates the result for an acquired slot.
    /// </summary>
    /// <param name="grant">The grant of the slot.</param>
    /// <returns>The acquired result.</returns>
    /// <exception cref="ArgumentNullException">The grant is null.</exception>
    public static SemaphoreAcquireResult Acquired(SemaphoreGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new SemaphoreAcquireResult(SemaphoreAcquireStatus.Acquired, grant);
    }
}
