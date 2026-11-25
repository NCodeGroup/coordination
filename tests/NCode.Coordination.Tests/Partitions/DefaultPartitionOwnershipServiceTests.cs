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
        var status = await services
            .GetRequiredService<IMembershipService>()
            .JoinAsync(group, member, null, cancellationToken);

        Assert.Equal(MembershipJoinStatus.Joined, status);

        return member;
    }

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WhenMembersJoined_AssignsEveryPartition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var group = CreateGroup();
        await JoinAsync(services, group, "node-a", cancellationToken);
        await JoinAsync(services, group, "node-b", cancellationToken);
        var partitions = new DefaultPartitionOwnershipService(
            services.GetRequiredService<IMembershipService>()
        );

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
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var group = CreateGroup();
        var memberA = await JoinAsync(services, group, "node-a", cancellationToken);
        var memberB = await JoinAsync(services, group, "node-b", cancellationToken);
        var memberships = services.GetRequiredService<IMembershipService>();
        var partitions = new DefaultPartitionOwnershipService(memberships);
        await using var enumerator = partitions
            .WatchAsync(group, 8, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var first = enumerator.Current;

        Assert.NotEmpty(first.GetPartitions("node-a"));
        Assert.NotEmpty(first.GetPartitions("node-b"));

        await memberships.LeaveAsync(group, memberB.Session, null, cancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var next = enumerator.Current;

        Assert.Equal(8, next.GetPartitions(memberA.NodeId).Length);
        Assert.Empty(next.GetPartitions("node-b"));
    }

    [Fact]
    public async Task WatchAsync_WhenThePartitionCountIsZero_ThrowsBeforeEnumerating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var partitions = new DefaultPartitionOwnershipService(
            services.GetRequiredService<IMembershipService>()
        );

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            partitions.WatchAsync(CreateGroup(), 0, null, cancellationToken)
        );
    }

    #endregion
}
