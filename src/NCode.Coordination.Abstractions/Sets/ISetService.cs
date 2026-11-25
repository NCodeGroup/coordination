// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// The operations of sets: members under a key, each with its own expiry.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface ISetService
{
    /// <summary>
    /// Adds a member to a set.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SetAdd"/>. A present member's expiry is replaced. The operation fails with
    /// <see cref="CoordinationErrorCodes.LimitExceeded"/> beyond <see cref="CoordinationLimits.MaxSetMembers"/>
    /// members.
    /// </remarks>
    /// <param name="key">The key of the set.</param>
    /// <param name="member">The member to add.</param>
    /// <param name="timeToLive">How long the member stays in the set.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the member was absent, or false when it was already present.</returns>
    ValueTask<bool> AddAsync(
        CoordinationKey key,
        KeySegment member,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a member from a set.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SetRemove"/>.
    /// </remarks>
    /// <param name="key">The key of the set.</param>
    /// <param name="member">The member to remove.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>True when the member was present, or false when it was not.</returns>
    ValueTask<bool> RemoveAsync(
        CoordinationKey key,
        KeySegment member,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists the live members of a set.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SetList"/>. The members come in no order.
    /// </remarks>
    /// <param name="key">The key of the set.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The live members, empty when there are none.</returns>
    ValueTask<ImmutableArray<KeySegment>> ListAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes every member of a set.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.SetClear"/>.
    /// </remarks>
    /// <param name="key">The key of the set.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the set is empty.</returns>
    ValueTask ClearAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
