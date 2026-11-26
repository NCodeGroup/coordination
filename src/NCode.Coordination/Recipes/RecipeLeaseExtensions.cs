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
