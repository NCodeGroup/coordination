// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Recipes;

namespace NCode.Coordination.Locks;

/// <summary>
/// Locks resources by acquiring and waiting on their leases.
/// </summary>
/// <param name="leases">The lease operations the lock is built on.</param>
internal class DefaultLockService(ILeaseService leases) : ILockService
{
    /// <summary>
    /// Gets the lease operations that acquire the lock and wait for its release.
    /// </summary>
    private ILeaseService Leases { get; } = leases;

    /// <inheritdoc />
    public async ValueTask<LeaseGrant?> AcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(terms);

        return await Leases
            .AcquireWhenFreeAsync(resource, terms, options, cancellationToken)
            .ConfigureAwait(false);
    }
}
