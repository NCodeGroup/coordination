// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class PartitionAssignmentTests
{
    private const int Partitions = 64;

    private static Keyspace TestKeyspace { get; } = new("prod");

    private static CoordinationKey Group { get; } = TestKeyspace.CreateKey("workers");

    private static Member CreateMember(string nodeId) =>
        new(new SessionId(TestKeyspace, $"s-{nodeId}"), nodeId, "10.0.0.1:5000");

    private static MembershipView CreateView(params string[] nodeIds) =>
        new(Group, new Revision(1), nodeIds.Select(CreateMember));

    #region Assign Tests

    [Fact]
    public void Assign_WhenTwoMembersJoined_GivesPartitionsZeroAndSevenToTheHigherScore()
    {
        var assignment = PartitionAssignment.Assign(CreateView("node-a", "node-b"), 8);

        Assert.Equal("node-b", assignment.GetOwner(0)?.NodeId);
        Assert.Equal("node-b", assignment.GetOwner(7)?.NodeId);
    }

    [Fact]
    public void Assign_WhenThereAreNoMembers_LeavesEveryPartitionWithoutAnOwner()
    {
        var assignment = PartitionAssignment.Assign(CreateView(), Partitions);

        for (var partition = 0; partition < Partitions; partition++)
        {
            Assert.Null(assignment.GetOwner(partition));
        }
    }

    [Fact]
    public void Assign_WhenAMemberLeaves_KeepsEveryOtherOwner()
    {
        var before = PartitionAssignment.Assign(
            CreateView("node-a", "node-b", "node-c"),
            Partitions
        );

        var after = PartitionAssignment.Assign(CreateView("node-a", "node-b"), Partitions);

        var moved = 0;
        for (var partition = 0; partition < Partitions; partition++)
        {
            var owner = before.GetOwner(partition);
            Assert.NotNull(owner);

            if (owner.NodeId == "node-c")
            {
                moved++;
                continue;
            }

            Assert.Equal(owner.NodeId, after.GetOwner(partition)?.NodeId);
        }

        Assert.NotEqual(0, moved);
    }

    [Fact]
    public void Assign_WhenAMemberJoins_MovesPartitionsOnlyToTheNewMember()
    {
        var before = PartitionAssignment.Assign(CreateView("node-a", "node-b"), Partitions);

        var after = PartitionAssignment.Assign(
            CreateView("node-a", "node-b", "node-c"),
            Partitions
        );

        var moved = 0;
        for (var partition = 0; partition < Partitions; partition++)
        {
            var newOwner = after.GetOwner(partition);
            Assert.NotNull(newOwner);

            if (newOwner.NodeId == before.GetOwner(partition)?.NodeId)
            {
                continue;
            }

            moved++;
            Assert.Equal("node-c", newOwner.NodeId);
        }

        Assert.NotEqual(0, moved);
    }

    [Fact]
    public void Assign_WhenGivenAView_CarriesTheGroupAndTheRevision()
    {
        var view = CreateView("node-a");

        var assignment = PartitionAssignment.Assign(view, 8);

        Assert.Same(view.Group, assignment.Group);
        Assert.Same(view.Revision, assignment.Revision);
        Assert.Equal(8, assignment.PartitionCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PartitionAssignment.MaxPartitions + 1)]
    public void Assign_WhenThePartitionCountIsOutsideTheRange_Throws(int partitionCount)
    {
        var view = CreateView("node-a");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PartitionAssignment.Assign(view, partitionCount)
        );
    }

    [Fact]
    public void Assign_WhenTheViewIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PartitionAssignment.Assign(null!, 8));
    }

    #endregion

    #region GetPartitions Tests

    [Fact]
    public void GetPartitions_WhenEveryMemberIsAsked_CoversEveryPartitionOnce()
    {
        var assignment = PartitionAssignment.Assign(
            CreateView("node-a", "node-b", "node-c"),
            Partitions
        );

        var partitions = new[] { "node-a", "node-b", "node-c" }
            .SelectMany(nodeId => assignment.GetPartitions(nodeId))
            .Order()
            .ToArray();

        Assert.Equal(Enumerable.Range(0, Partitions), partitions);
    }

    [Fact]
    public void GetPartitions_WhenTheNodeOwnsPartitions_ReturnsThemInAscendingOrder()
    {
        var assignment = PartitionAssignment.Assign(CreateView("node-a", "node-b"), Partitions);

        var partitions = assignment.GetPartitions("node-b");

        Assert.NotEmpty(partitions);
        Assert.Equal<int>(partitions.Order().ToArray(), partitions);
    }

    [Fact]
    public void GetPartitions_WhenTheNodeIsUnknown_ReturnsEmpty()
    {
        var assignment = PartitionAssignment.Assign(CreateView("node-a"), Partitions);

        Assert.Empty(assignment.GetPartitions("node-z"));
    }

    #endregion

    #region GetOwner Tests

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void GetOwner_WhenThePartitionIsOutsideTheRange_Throws(int partition)
    {
        var assignment = PartitionAssignment.Assign(CreateView("node-a"), 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => assignment.GetOwner(partition));
    }

    #endregion
}
