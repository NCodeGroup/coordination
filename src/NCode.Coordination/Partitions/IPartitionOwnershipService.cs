// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Assigns the partitions of a workload to the live members of a group, built on membership.
/// </summary>
/// <remarks>
/// The assignment is that of the group's live members: current first, then one per membership view. A partition moves
/// only when membership changes, which is when its owner leaves or a joining member outscores it. Ownership is
/// advisory, so exclusive work on a partition also takes a lease. Failures and cancellation throw as the membership
/// operations do.
/// </remarks>
public interface IPartitionOwnershipService
{
    /// <summary>
    /// Reads the current assignment of the partitions of a group.
    /// </summary>
    /// <remarks>
    /// The assignment is computed from the group's live members at the revision of the view it read.
    /// </remarks>
    /// <param name="group">The group of members that own the partitions.</param>
    /// <param name="partitionCount">The number of partitions, from 1 to <see cref="PartitionAssignment.MaxPartitions"/>.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The assignment of every partition to a member, or to none when the group has no members.</returns>
    ValueTask<PartitionAssignment> ReadAsync(
        CoordinationKey group,
        int partitionCount,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Streams the assignment of the partitions of a group as its members change.
    /// </summary>
    /// <remarks>
    /// The stream yields the current assignment first, then one for each later membership view. The arguments are
    /// validated when the method is called, before the stream is enumerated.
    /// </remarks>
    /// <param name="group">The group of members that own the partitions.</param>
    /// <param name="partitionCount">The number of partitions, from 1 to <see cref="PartitionAssignment.MaxPartitions"/>.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A stream of assignments.</returns>
    IAsyncEnumerable<PartitionAssignment> WatchAsync(
        CoordinationKey group,
        int partitionCount,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
