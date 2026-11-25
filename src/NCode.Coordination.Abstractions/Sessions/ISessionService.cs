// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of sessions: a node's liveness, which ends everything bound to it when it ends.
/// Memberships, leases, semaphore slots, elections and records can be bound to a session.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface ISessionService
{
    /// <summary>
    /// Opens a session in a keyspace.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SessionOpen"/>. The provider rounds the time to live up to its server's unit
    /// and minimum, and refuses one above its server's maximum with
    /// <see cref="CoordinationErrorCodes.LimitExceeded"/>.
    /// </remarks>
    /// <param name="keyspace">The keyspace to open the session in.</param>
    /// <param name="timeToLive">The time to live the session asks for.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The session's identity and the time to live the provider granted.</returns>
    ValueTask<SessionGrant> OpenAsync(
        Keyspace keyspace,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Renews a session for its granted time to live.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SessionKeepAlive"/>. The result is ended when the session has ended.
    /// </remarks>
    /// <param name="session">The session to renew.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Renewed with the granted time to live, or ended.</returns>
    ValueTask<KeepAliveResult> KeepAliveAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Ends a session and everything bound to it.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SessionClose"/>. Closing a session that has ended succeeds.
    /// </remarks>
    /// <param name="session">The session to close.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the session has ended.</returns>
    ValueTask CloseAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Waits until a session ends.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SessionWaitForEnd"/>. It completes when the session ends, by a close or by
    /// expiry, and at once when the session has ended.
    /// </remarks>
    /// <param name="session">The session to wait for.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the session has ended.</returns>
    ValueTask WaitForEndAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
