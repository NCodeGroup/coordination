// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Partitions;

namespace NCode.Coordination;

/// <summary>
/// The owner of each partition of a group, one member for each partition, or none when the group has no members.
/// </summary>
/// <remarks>
/// Each partition is owned by the member with the highest score for it, so a partition moves only when its owner
/// leaves or a joining member outscores it. The assignment is advisory: work that must be exclusive also takes a lease.
/// </remarks>
public sealed class PartitionAssignment
{
    /// <summary>
    /// The largest number of partitions an assignment has.
    /// </summary>
    public const int MaxPartitions = 4_096;

    private PartitionAssignment(
        CoordinationKey group,
        Revision revision,
        int partitionCount,
        ImmutableArray<Member?> owners
    )
    {
        Group = group;
        Revision = revision;
        PartitionCount = partitionCount;
        Owners = owners;
    }

    /// <summary>
    /// Gets the group the assignment is of.
    /// </summary>
    public CoordinationKey Group { get; }

    /// <summary>
    /// Gets the revision of the membership the assignment was computed from.
    /// </summary>
    public Revision Revision { get; }

    /// <summary>
    /// Gets the number of partitions.
    /// </summary>
    public int PartitionCount { get; }

    private ImmutableArray<Member?> Owners { get; }

    /// <summary>
    /// Returns the member that owns a partition.
    /// </summary>
    /// <param name="partition">The partition number, from 0 to one less than <see cref="PartitionCount"/>.</param>
    /// <returns>The owner, or null when the group had no members.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The partition is outside the range.</exception>
    public Member? GetOwner(int partition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(partition);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(partition, PartitionCount);

        return Owners[partition];
    }

    /// <summary>
    /// Returns the partitions a node owns.
    /// </summary>
    /// <param name="nodeId">The node identifier of the member, compared ordinally.</param>
    /// <returns>The partition numbers in ascending order, empty when the node owns none.</returns>
    /// <exception cref="ArgumentNullException">The node identifier is null.</exception>
    public ImmutableArray<int> GetPartitions(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        var partitions = ImmutableArray.CreateBuilder<int>();

        for (var partition = 0; partition < Owners.Length; partition++)
        {
            if (string.Equals(Owners[partition]?.NodeId, nodeId, StringComparison.Ordinal))
            {
                partitions.Add(partition);
            }
        }

        return partitions.ToImmutable();
    }

    /// <summary>
    /// Assigns every partition to the member with the highest score for it.
    /// </summary>
    /// <param name="view">The members of the group.</param>
    /// <param name="partitionCount">The number of partitions, from 1 to <see cref="MaxPartitions"/>.</param>
    /// <returns>The assignment, in which a tie goes to the member with the ordinally smaller node identifier.</returns>
    /// <exception cref="ArgumentNullException">The view is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The partition count is outside the range.</exception>
    public static PartitionAssignment Assign(MembershipView view, int partitionCount)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(partitionCount, MaxPartitions);

        var owners = ImmutableArray.CreateBuilder<Member?>(partitionCount);

        for (var partition = 0; partition < partitionCount; partition++)
        {
            Member? owner = null;
            var ownerScore = 0UL;

            foreach (var member in view.Members)
            {
                var score = RendezvousHash.Score(member.NodeId, partition);

                if (
                    owner is null
                    || score > ownerScore
                    || (
                        score == ownerScore
                        && string.CompareOrdinal(member.NodeId, owner.NodeId) < 0
                    )
                )
                {
                    owner = member;
                    ownerScore = score;
                }
            }

            owners.Add(owner);
        }

        return new PartitionAssignment(
            view.Group,
            view.Revision,
            partitionCount,
            owners.MoveToImmutable()
        );
    }
}
