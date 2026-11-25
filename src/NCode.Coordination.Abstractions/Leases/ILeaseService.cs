// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of leases: exclusive ownership of a resource, proven by a fencing token.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface ILeaseService
{
    /// <summary>
    /// Tries to acquire the lease on a resource.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.LeaseTryAcquire"/>. It never waits: the result is held when another holder
    /// has the lease. Acquiring again with the session of a grant already held returns that grant. The fencing token
    /// is greater than every earlier grant's of the resource.
    /// </remarks>
    /// <param name="resource">The resource to lease.</param>
    /// <param name="terms">How long the grant lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Acquired with the grant, held, or session ended.</returns>
    ValueTask<LeaseAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Renews a grant made for a time to live.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.LeaseRenew"/>. It extends the grant by a new duration from when the server
    /// applies it. A grant bound to a session throws <see cref="ArgumentException"/>, since it lives as its session
    /// does.
    /// </remarks>
    /// <param name="grant">The grant to renew.</param>
    /// <param name="timeToLive">The duration to extend the grant by.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the grant was renewed; false when it has ended.</returns>
    ValueTask<bool> RenewAsync(
        LeaseGrant grant,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Releases a grant.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.LeaseRelease"/>. It releases only this grant, by its owner token.
    /// </remarks>
    /// <param name="grant">The grant to release.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the grant was released; false when it had already ended.</returns>
    ValueTask<bool> ReleaseAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Waits until a resource has no holder.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.LeaseWaitForRelease"/>. It completes once the resource has no holder, and
    /// at once when it has none. Another caller may acquire the lease first.
    /// </remarks>
    /// <param name="resource">The resource to wait for.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the resource has no holder.</returns>
    ValueTask WaitForReleaseAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
