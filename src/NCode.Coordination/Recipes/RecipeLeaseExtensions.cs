// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Recipes;

/// <summary>
/// Lease operations the recipes share.
/// </summary>
internal static class RecipeLeaseExtensions
{
    extension(ILeaseService leases)
    {
        /// <summary>
        /// Acquires a lease, waiting for its release while another holder has it.
        /// </summary>
        /// <param name="resource">The key of the lease to acquire.</param>
        /// <param name="terms">The terms of the lease.</param>
        /// <param name="options">Options for the call, or null for none.</param>
        /// <param name="cancellationToken">The token that cancels the wait.</param>
        /// <returns>The grant, or null when the session the terms name has ended.</returns>
        public async ValueTask<LeaseGrant?> AcquireWhenFreeAsync(
            CoordinationKey resource,
            LeaseTerms terms,
            OperationOptions? options,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(leases);

            while (true)
            {
                var result = await leases
                    .TryAcquireAsync(resource, terms, options, cancellationToken)
                    .ConfigureAwait(false);

                switch (result.Status)
                {
                    case LeaseAcquireStatus.Acquired:
                        return result.Grant;
                    case LeaseAcquireStatus.SessionEnded:
                        return null;
                    default:
                        await leases
                            .WaitForReleaseAsync(resource, options, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                }
            }
        }

        /// <summary>
        /// Gives a lease back, treating a provider failure as the lease ending with its time to live or its session.
        /// </summary>
        /// <param name="grant">The grant of the lease to give back.</param>
        /// <param name="options">Options for the call, or null for none.</param>
        /// <param name="cancellationToken">The token that cancels the call.</param>
        /// <returns>True when the lease was released; false when it was not, or the provider failed.</returns>
        public async ValueTask<bool> ReleaseQuietlyAsync(
            LeaseGrant grant,
            OperationOptions? options,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(leases);

            try
            {
                return await leases
                    .ReleaseAsync(grant, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CoordinationException)
            {
                // The lease still ends with its time to live or its session.
                return false;
            }
        }
    }
}
