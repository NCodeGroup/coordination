// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of membership: the live sessions of a group, with each node's identity, address and metadata.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface IMembershipService
{
    /// <summary>
    /// Adds a member to a group.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.MembershipJoin"/>. The node id is taken when another live session holds
    /// it. A session that joins again replaces its entry. The entry ends with the session.
    /// </remarks>
    /// <param name="group">The group to join.</param>
    /// <param name="member">The member to add.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Joined, node id taken, or session ended.</returns>
    ValueTask<MembershipJoinStatus> JoinAsync(
        CoordinationKey group,
        Member member,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a session's entry from a group.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.MembershipLeave"/>. Leaving a group the session is not in succeeds.
    /// </remarks>
    /// <param name="group">The group to leave.</param>
    /// <param name="session">The session whose entry is removed.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the entry is gone.</returns>
    ValueTask LeaveAsync(
        CoordinationKey group,
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads the members of a group.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.MembershipRead"/>.
    /// </remarks>
    /// <param name="group">The group to read.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The members of the group and the revision of the view.</returns>
    ValueTask<MembershipView> ReadAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Streams the views of a group as its members change.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.MembershipWatch"/>. The stream yields the current view first, then each
    /// later view at a greater revision, possibly skipping intermediate ones.
    /// </remarks>
    /// <param name="group">The group to watch.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A stream of views.</returns>
    IAsyncEnumerable<MembershipView> WatchAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
