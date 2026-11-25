// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination;

/// <summary>
/// The result of trying to acquire a lease: acquired with its grant, held by another holder, or the session ended.
/// </summary>
/// <remarks>
/// Create an acquired result with <see cref="Acquired(LeaseGrant)"/>; <see cref="Held"/> and
/// <see cref="SessionEnded"/> are shared.
/// </remarks>
public sealed class LeaseAcquireResult
{
    /// <summary>
    /// Gets the result for a lease another holder has.
    /// </summary>
    public static LeaseAcquireResult Held { get; } = new(LeaseAcquireStatus.Held, null);

    /// <summary>
    /// Gets the result for a session that has ended.
    /// </summary>
    public static LeaseAcquireResult SessionEnded { get; } =
        new(LeaseAcquireStatus.SessionEnded, null);

    private LeaseAcquireResult(LeaseAcquireStatus status, LeaseGrant? grant)
    {
        Status = status;
        Grant = grant;
    }

    /// <summary>
    /// Gets the outcome of the attempt.
    /// </summary>
    public LeaseAcquireStatus Status { get; }

    /// <summary>
    /// Gets the grant of the acquired lease, or null when it was not acquired.
    /// </summary>
    public LeaseGrant? Grant { get; }

    /// <summary>
    /// Gets a value indicating whether the lease was acquired.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Grant))]
    public bool IsAcquired => Status == LeaseAcquireStatus.Acquired;

    /// <summary>
    /// Creates the result for an acquired lease.
    /// </summary>
    /// <param name="grant">The grant of the lease.</param>
    /// <returns>The acquired result.</returns>
    /// <exception cref="ArgumentNullException">The grant is null.</exception>
    public static LeaseAcquireResult Acquired(LeaseGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new LeaseAcquireResult(LeaseAcquireStatus.Acquired, grant);
    }
}
