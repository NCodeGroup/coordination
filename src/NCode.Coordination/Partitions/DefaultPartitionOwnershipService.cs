// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace NCode.Coordination.Partitions;

/// <summary>
/// Assigns the partitions of a workload to the live members of a group, computed from the group's membership views.
/// </summary>
/// <param name="membership">The membership operations the assignment is built on.</param>
internal class DefaultPartitionOwnershipService(IMembershipService membership)
    : IPartitionOwnershipService
{
    private IMembershipService Membership { get; } = membership;

    /// <inheritdoc />
    public async ValueTask<PartitionAssignment> ReadAsync(
        CoordinationKey group,
        int partitionCount,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        Validate(group, partitionCount);

        var view = await Membership
            .ReadAsync(group, options, cancellationToken)
            .ConfigureAwait(false);

        return PartitionAssignment.Assign(view, partitionCount);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PartitionAssignment> WatchAsync(
        CoordinationKey group,
        int partitionCount,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        Validate(group, partitionCount);

        return WatchCoreAsync(group, partitionCount, options, cancellationToken);
    }

    /// <summary>
    /// Validates the arguments that both operations share, before any call to the membership operations.
    /// </summary>
    /// <param name="group">The group of members that own the partitions.</param>
    /// <param name="partitionCount">The number of partitions.</param>
    private static void Validate(CoordinationKey group, int partitionCount)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            partitionCount,
            PartitionAssignment.MaxPartitions
        );
    }

    /// <summary>
    /// Streams the assignment computed from each membership view of the group.
    /// </summary>
    /// <param name="group">The group of members that own the partitions.</param>
    /// <param name="partitionCount">The number of partitions.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of assignments.</returns>
    private async IAsyncEnumerable<PartitionAssignment> WatchCoreAsync(
        CoordinationKey group,
        int partitionCount,
        OperationOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (
            var view in Membership
                .WatchAsync(group, options, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            yield return PartitionAssignment.Assign(view, partitionCount);
        }
    }
}
