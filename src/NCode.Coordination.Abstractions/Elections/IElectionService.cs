// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of elections: one leader of a role at a time, whose term is its fencing token.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface IElectionService
{
    /// <summary>
    /// Tries to become the leader of a role.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.ElectionTryLead"/>. It never waits: the result is led when another node
    /// leads. Trying to lead again with the session of the leadership already held returns that leadership. The
    /// value, at most <see cref="CoordinationLimits.MaxValueLength"/> bytes, is what followers read, such
    /// as the leader's address. The provider reads the value before the returned task completes, so the caller may
    /// reuse its buffer once it does.
    /// </remarks>
    /// <param name="role">The role to lead.</param>
    /// <param name="value">The value followers read.</param>
    /// <param name="terms">How long the leadership lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Elected with the leadership, led by another node, or session ended.</returns>
    ValueTask<ElectionResult> TryLeadAsync(
        CoordinationKey role,
        ReadOnlyMemory<byte> value,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Renews a leadership made for a time to live.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.ElectionRenew"/>. It renews as <see cref="ILeaseService"/> renews a grant:
    /// it extends the leadership by a new duration from when the server applies it, and a leadership bound to a
    /// session throws <see cref="ArgumentException"/>.
    /// </remarks>
    /// <param name="leadership">The leadership to renew.</param>
    /// <param name="timeToLive">The duration to extend the leadership by.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the leadership was renewed; false when it has ended.</returns>
    ValueTask<bool> RenewAsync(
        Leadership leadership,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gives up a leadership.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.ElectionResign"/>. It resigns only this leadership, by its owner token.
    /// </remarks>
    /// <param name="leadership">The leadership to give up.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the leadership was given up; false when the caller did not lead.</returns>
    ValueTask<bool> ResignAsync(
        Leadership leadership,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads the current leader of a role.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.ElectionRead"/>.
    /// </remarks>
    /// <param name="role">The role to read.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The view of the role, whose leader is null when it has none.</returns>
    ValueTask<ElectionView> ReadAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Streams the views of a role as its leader changes.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.ElectionWatch"/>. The stream yields the current view first, then each
    /// later term or vacancy, possibly skipping intermediate ones.
    /// </remarks>
    /// <param name="role">The role to watch.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A stream of views.</returns>
    IAsyncEnumerable<ElectionView> WatchAsync(
        CoordinationKey role,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
