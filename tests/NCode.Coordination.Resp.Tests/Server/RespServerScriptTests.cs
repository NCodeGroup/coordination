// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespServerScriptTests(RespServers servers)
{
    private static RespScript FailAfterWrite { get; } =
        new("fail", "redis.call('SET', KEYS[1], '1') return redis.error_reply('ERR stop')");

    private static RespConnection CreateConnection(string endpoint) =>
        new(
            async ct => await ConnectionMultiplexer.ConnectAsync(endpoint).WaitAsync(ct),
            owned: true
        );

    #region EvaluateAsync Tests

    [RespServerFact]
    public async Task EvaluateAsync_ValkeyScriptErrorAfterWrite_ThrowsOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = CreateConnection(servers.Valkey);
        var server = new RespServer(connection);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.EvaluateAsync(FailAfterWrite, ["resp-tests:fail"], [], cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    [RespServerFact]
    public async Task EvaluateAsync_GarnetScriptErrorAfterWrite_ThrowsOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = CreateConnection(servers.Garnet);
        var server = new RespServer(connection);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.EvaluateAsync(FailAfterWrite, ["resp-tests:fail"], [], cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    #endregion

    #region ExecuteAsync Tests

    [RespServerFact]
    public async Task ExecuteAsync_Valkey_ReturnsPong()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = CreateConnection(servers.Valkey);
        var server = new RespServer(connection);

        var result = await server.ExecuteAsync("PING", [], cancellationToken);

        var text = result.ToString();

        Assert.Equal("PONG", text);
    }

    #endregion
}
