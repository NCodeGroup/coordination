// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of semaphores: up to a capacity of holders of a resource, each slot proven by its own fencing token.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface ISemaphoreService
{
    /// <summary>
    /// Tries to acquire a slot of the semaphore on a resource.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SemaphoreTryAcquire"/>. It never waits: the result is full when every slot
    /// is held, and a capacity conflict when the holders use another capacity. The capacity is 1 to
    /// <see cref="CoordinationLimits.MaxSemaphoreCapacity"/>, else <see cref="ArgumentOutOfRangeException"/>. The
    /// fencing token of the slot is greater than every earlier grant's of that slot.
    /// </remarks>
    /// <param name="resource">The resource to hold a slot of.</param>
    /// <param name="capacity">The number of slots, named on every acquire.</param>
    /// <param name="terms">How long the grant lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Acquired with the grant, full, a capacity conflict, or session ended.</returns>
    ValueTask<SemaphoreAcquireResult> TryAcquireAsync(
        CoordinationKey resource,
        int capacity,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Renews a grant made for a time to live.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SemaphoreRenew"/>. It extends the grant by a new duration from when the
    /// server applies it. A grant bound to a session throws <see cref="ArgumentException"/>, since it lives as its
    /// session does.
    /// </remarks>
    /// <param name="grant">The grant to renew.</param>
    /// <param name="timeToLive">The duration to extend the grant by.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the grant was renewed; false when it has ended.</returns>
    ValueTask<bool> RenewAsync(
        SemaphoreGrant grant,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Releases a grant.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SemaphoreRelease"/>. It releases only this grant, by its owner token.
    /// </remarks>
    /// <param name="grant">The grant to release.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the grant was released; false when it had already ended.</returns>
    ValueTask<bool> ReleaseAsync(
        SemaphoreGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Waits until a slot of the semaphore is free.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SemaphoreWaitForRelease"/>. It completes once a slot is free, and at once
    /// when one is. Another caller may acquire the slot first.
    /// </remarks>
    /// <param name="resource">The resource to wait for.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when a slot is free.</returns>
    ValueTask WaitForReleaseAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
