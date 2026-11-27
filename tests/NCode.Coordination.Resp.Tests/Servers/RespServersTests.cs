// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests.Servers;

public sealed class RespServersTests(RespServers servers)
{
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
    public async Task ValkeyCluster_Started_ServesEverySlotFromThreeNodes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = ConfigurationOptions.Parse(servers.ValkeyCluster);
        options.AllowAdmin = true;
        await using var connection = await ConnectionMultiplexer
            .ConnectAsync(options)
            .WaitAsync(cancellationToken);

        var clusterServers = connection.GetServers();
        var executing = clusterServers[0].ExecuteAsync("CLUSTER", "INFO");
        var result = await executing.WaitAsync(cancellationToken);
        var info = result.ToString();

        Assert.Equal(3, clusterServers.Length);
        Assert.Contains("cluster_state:ok", info);
        Assert.Contains("cluster_slots_assigned:16384", info);
    }

    #endregion

    #region GarnetCluster Property Tests

    [RespServerFact]
    public async Task GarnetCluster_Started_ServesEverySlotFromThreeNodes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = ConfigurationOptions.Parse(servers.GarnetCluster);
        options.AllowAdmin = true;
        await using var connection = await ConnectionMultiplexer
            .ConnectAsync(options)
            .WaitAsync(cancellationToken);

        var clusterServers = connection.GetServers();
        var executing = clusterServers[0].ExecuteAsync("CLUSTER", "INFO");
        var result = await executing.WaitAsync(cancellationToken);
        var info = result.ToString();

        Assert.Equal(3, clusterServers.Length);
        Assert.Contains("cluster_state:ok", info);
        Assert.Contains("cluster_slots_assigned:16384", info);
    }

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
