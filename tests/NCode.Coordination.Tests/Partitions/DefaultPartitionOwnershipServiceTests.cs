// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.Partitions.Tests;

public sealed class DefaultPartitionOwnershipServiceTests
{
    private static CoordinationKey CreateGroup() => RecipeTesting.Keyspace.CreateKey("workers");

    private static async Task<Member> JoinAsync(
        IServiceProvider services,
        CoordinationKey group,
        string nodeId,
        CancellationToken cancellationToken
    )
    {
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        var member = new Member(session, nodeId, $"{nodeId}:5000");
        var memberships = services.GetRequiredService<IMembershipService>();
        var status = await memberships.JoinAsync(group, member, null, cancellationToken);

        Assert.Equal(MembershipJoinStatus.Joined, status);

        return member;
    }

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WhenMembersJoined_AssignsEveryPartition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var group = CreateGroup();
        await JoinAsync(services, group, "node-a", cancellationToken);
        await JoinAsync(services, group, "node-b", cancellationToken);
        var memberships = services.GetRequiredService<IMembershipService>();
        var partitions = new DefaultPartitionOwnershipService(memberships);

        var assignment = await partitions.ReadAsync(group, 8, null, cancellationToken);

        Assert.Equal(8, assignment.PartitionCount);
        for (var partition = 0; partition < 8; partition++)
        {
            var owner = assignment.GetOwner(partition);

            Assert.NotNull(owner);
            Assert.Contains(owner.NodeId, new[] { "node-a", "node-b" });
        }
    }

    #endregion

    #region WatchAsync Tests

    [Fact]
    public async Task WatchAsync_WhenAMemberLeaves_YieldsTheNewAssignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var group = CreateGroup();
        var memberA = await JoinAsync(services, group, "node-a", cancellationToken);
        var memberB = await JoinAsync(services, group, "node-b", cancellationToken);
        var memberships = services.GetRequiredService<IMembershipService>();
        var partitions = new DefaultPartitionOwnershipService(memberships);
        var watch = partitions.WatchAsync(group, 8, null, cancellationToken);
        await using var enumerator = watch.GetAsyncEnumerator(cancellationToken);

        var movedFirst = await enumerator.MoveNextAsync();
        var first = enumerator.Current;
        var firstPartitionsA = first.GetPartitions("node-a");
        var firstPartitionsB = first.GetPartitions("node-b");

        Assert.True(movedFirst);
        Assert.NotEmpty(firstPartitionsA);
        Assert.NotEmpty(firstPartitionsB);

        await memberships.LeaveAsync(group, memberB.Session, null, cancellationToken);

        var movedNext = await enumerator.MoveNextAsync();
        var next = enumerator.Current;
        var nextPartitionsA = next.GetPartitions(memberA.NodeId);
        var nextPartitionsB = next.GetPartitions("node-b");

        Assert.True(movedNext);
        Assert.Equal(8, nextPartitionsA.Length);
        Assert.Empty(nextPartitionsB);
    }

    [Fact]
    public async Task WatchAsync_WhenThePartitionCountIsZero_ThrowsBeforeEnumerating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var memberships = services.GetRequiredService<IMembershipService>();
        var partitions = new DefaultPartitionOwnershipService(memberships);
        var group = CreateGroup();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            partitions.WatchAsync(group, 0, null, cancellationToken)
        );

        Assert.Equal("partitionCount", exception.ParamName);
    }

    #endregion
}
