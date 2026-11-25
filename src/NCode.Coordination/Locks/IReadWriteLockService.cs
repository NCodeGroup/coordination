// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A read-write lock on a resource, built on leases and records: many readers or one writer.
/// </summary>
/// <remarks>
/// Waits have no fairness, so constant readers can starve a writer. A read grant's fencing token orders it among the
/// write grants: it is greater than every earlier write grant's and less than every later one's. There is no upgrade
/// from a read lock to a write lock and no reentrancy. The keys under the resource belong to the lock. Failures and
/// cancellation throw as the lease and record operations do.
/// </remarks>
public interface IReadWriteLockService
{
    /// <summary>
    /// Waits until no writer holds or awaits the resource, then takes a read lock on it.
    /// </summary>
    /// <remarks>
    /// It returns the grant, whose fencing token the resource checks, or null when the session in the terms has ended.
    /// The caller releases the grant with <see cref="ReleaseReadAsync"/>.
    /// </remarks>
    /// <param name="resource">The resource to lock.</param>
    /// <param name="terms">How long the grant lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The grant, or null when the session in the terms has ended.</returns>
    ValueTask<ReadLockGrant?> AcquireReadAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Releases a read lock.
    /// </summary>
    /// <param name="grant">The grant to release.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the grant was released, and false when it had already ended.</returns>
    ValueTask<bool> ReleaseReadAsync(
        ReadLockGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Waits until no other writer holds the resource and no reader holds it, then takes a write lock on it.
    /// </summary>
    /// <remarks>
    /// It returns the grant, whose fencing token the resource checks, or null when the session in the terms has ended.
    /// The caller renews the grant with <see cref="ILeaseService"/> and releases it with
    /// <see cref="ReleaseWriteAsync"/>.
    /// </remarks>
    /// <param name="resource">The resource to lock.</param>
    /// <param name="terms">How long the grant lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The grant, or null when the session in the terms has ended.</returns>
    ValueTask<LeaseGrant?> AcquireWriteAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Releases a write lock.
    /// </summary>
    /// <param name="grant">The grant to release.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the grant was released, and false when it had already ended.</returns>
    ValueTask<bool> ReleaseWriteAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
