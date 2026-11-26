// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A lock on a resource, built on leases.
/// </summary>
public interface ILockService
{
    /// <summary>
    /// Waits until the lease on a resource is free, then acquires it.
    /// </summary>
    /// <remarks>
    /// It waits until the lease is free, with no fairness, and returns the grant, whose fencing token the resource
    /// checks, or null when the session in the terms has ended. The caller renews and releases the grant with
    /// <see cref="ILeaseService"/>. Failures and cancellation throw as the lease operations do.
    /// </remarks>
    /// <param name="resource">The resource to lock.</param>
    /// <param name="terms">How long the grant lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The grant, or null when the session in the terms has ended.</returns>
    ValueTask<LeaseGrant?> AcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
