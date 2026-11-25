// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class PartitionAssignmentTests
{
    private const int Partitions = 64;

    private static Keyspace TestKeyspace { get; } = new("prod");

    private static CoordinationKey Group { get; } = TestKeyspace.CreateKey("workers");

    private static Member CreateMember(string nodeId)
    {
        var session = new SessionId(TestKeyspace, $"s-{nodeId}");

        return new Member(session, nodeId, "10.0.0.1:5000");
    }

    private static MembershipView CreateView(params string[] nodeIds)
    {
        var revision = new Revision(1);
        var members = nodeIds.Select(CreateMember);

        return new MembershipView(Group, revision, members);
    }

    #region Assign Tests

    [Fact]
    public void Assign_WhenTwoMembersJoined_GivesPartitionsZeroAndSevenToTheHigherScore()
    {
        var view = CreateView("node-a", "node-b");
        var assignment = PartitionAssignment.Assign(view, 8);

        var ownerOfZero = assignment.GetOwner(0);
        var ownerOfSeven = assignment.GetOwner(7);

        Assert.Equal("node-b", ownerOfZero?.NodeId);
        Assert.Equal("node-b", ownerOfSeven?.NodeId);
    }

    [Fact]
    public void Assign_WhenThereAreNoMembers_LeavesEveryPartitionWithoutAnOwner()
    {
        var view = CreateView();
        var assignment = PartitionAssignment.Assign(view, Partitions);

        for (var partition = 0; partition < Partitions; partition++)
        {
            var owner = assignment.GetOwner(partition);

            Assert.Null(owner);
        }
    }

    [Fact]
    public void Assign_WhenAMemberLeaves_KeepsEveryOtherOwner()
    {
        var viewBefore = CreateView("node-a", "node-b", "node-c");
        var before = PartitionAssignment.Assign(viewBefore, Partitions);
        var viewAfter = CreateView("node-a", "node-b");
        var after = PartitionAssignment.Assign(viewAfter, Partitions);

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

            var ownerAfter = after.GetOwner(partition);

            Assert.Equal(owner.NodeId, ownerAfter?.NodeId);
        }

        Assert.NotEqual(0, moved);
    }

    [Fact]
    public void Assign_WhenAMemberJoins_MovesPartitionsOnlyToTheNewMember()
    {
        var viewBefore = CreateView("node-a", "node-b");
        var before = PartitionAssignment.Assign(viewBefore, Partitions);
        var viewAfter = CreateView("node-a", "node-b", "node-c");
        var after = PartitionAssignment.Assign(viewAfter, Partitions);

        var moved = 0;
        for (var partition = 0; partition < Partitions; partition++)
        {
            var newOwner = after.GetOwner(partition);
            Assert.NotNull(newOwner);

            var oldOwner = before.GetOwner(partition);
            if (newOwner.NodeId == oldOwner?.NodeId)
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
        var view = CreateView("node-a", "node-b", "node-c");
        var assignment = PartitionAssignment.Assign(view, Partitions);
        var nodeIds = new[] { "node-a", "node-b", "node-c" };

        var covered = nodeIds
            .SelectMany(nodeId => assignment.GetPartitions(nodeId))
            .Order()
            .ToArray();

        var expected = Enumerable.Range(0, Partitions);

        Assert.Equal(expected, covered);
    }

    [Fact]
    public void GetPartitions_WhenTheNodeOwnsPartitions_ReturnsThemInAscendingOrder()
    {
        var view = CreateView("node-a", "node-b");
        var assignment = PartitionAssignment.Assign(view, Partitions);

        var partitions = assignment.GetPartitions("node-b");

        var sorted = partitions.Order().ToArray();

        Assert.NotEmpty(partitions);
        Assert.Equal<int>(sorted, partitions);
    }

    [Fact]
    public void GetPartitions_WhenTheNodeIsUnknown_ReturnsEmpty()
    {
        var view = CreateView("node-a");
        var assignment = PartitionAssignment.Assign(view, Partitions);

        var partitions = assignment.GetPartitions("node-z");

        Assert.Empty(partitions);
    }

    #endregion

    #region GetOwner Tests

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void GetOwner_WhenThePartitionIsOutsideTheRange_Throws(int partition)
    {
        var view = CreateView("node-a");
        var assignment = PartitionAssignment.Assign(view, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => assignment.GetOwner(partition));
    }

    #endregion
}
