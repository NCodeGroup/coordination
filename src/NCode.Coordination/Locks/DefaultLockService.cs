// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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

        while (true)
        {
            var result = await Leases
                .TryAcquireAsync(resource, terms, options, cancellationToken)
                .ConfigureAwait(false);

            switch (result.Status)
            {
                case LeaseAcquireStatus.Acquired:
                    return result.Grant;
                case LeaseAcquireStatus.SessionEnded:
                    return null;
                default:
                    await Leases
                        .WaitForReleaseAsync(resource, options, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
    }
}
