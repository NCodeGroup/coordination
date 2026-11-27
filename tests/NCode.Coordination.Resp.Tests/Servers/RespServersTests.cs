// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests.Servers;

public sealed class RespServersTests(RespServers servers)
{
    /// <summary>
    /// The line of <c>CLUSTER INFO</c> that says every one of a cluster's slots is assigned.
    /// </summary>
    private const string SlotsAssigned = "cluster_slots_assigned:16384";

    /// <summary>
    /// Checks that a cluster has three nodes, that it serves its slots and that every slot is assigned.
    /// </summary>
    /// <param name="endpoint">The <c>host:port</c> list of the cluster's nodes.</param>
    /// <param name="cancellationToken">The token that cancels the checks.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertServesEverySlotAsync(
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await RespNodes.ConnectAdminAsync(endpoint, cancellationToken);

        var clusterServers = connection.GetServers();
        var executing = clusterServers[0]
            .ExecuteAsync(RespNodes.ClusterCommand, RespNodes.InfoCommand);
        var result = await executing.WaitAsync(cancellationToken);
        var info = result.ToString();

        Assert.Equal(3, clusterServers.Length);
        Assert.Contains(RespNodes.ClusterStateOk, info);
        Assert.Contains(SlotsAssigned, info);
    }

    #region Valkey Property Tests

    [RespServerFact]
    public async Task Valkey_Started_AnswersPing()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(servers.Valkey);

        var latency = await connection.GetDatabase().PingAsync();

        Assert.True(latency >= TimeSpan.Zero);
    }

    #endregion

    #region ValkeyCluster Property Tests

    [RespServerFact]
    public async Task ValkeyCluster_Started_ServesEverySlotFromThreeNodes() =>
        await AssertServesEverySlotAsync(
            servers.ValkeyCluster,
            TestContext.Current.CancellationToken
        );

    #endregion

    #region GarnetCluster Property Tests

    [RespServerFact]
    public async Task GarnetCluster_Started_ServesEverySlotFromThreeNodes() =>
        await AssertServesEverySlotAsync(
            servers.GarnetCluster,
            TestContext.Current.CancellationToken
        );

    #endregion

    #region Garnet Property Tests

    [RespServerFact]
    public async Task Garnet_Started_AnswersPing()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(servers.Garnet);

        var latency = await connection.GetDatabase().PingAsync();

        Assert.True(latency >= TimeSpan.Zero);
    }

    #endregion
}
