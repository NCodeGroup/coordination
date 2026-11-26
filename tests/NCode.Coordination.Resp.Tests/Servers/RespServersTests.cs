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
