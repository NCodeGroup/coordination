// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespServerTests(RespServers servers) : IDisposable
{
    private static RespScript FailAfterWrite { get; } =
        new("fail", "redis.call('SET', KEYS[1], '1') return redis.error_reply('ERR stop')");

    private static RespConnection CreateConnection(string endpoint) =>
        new(
            async ct =>
            {
                var connecting = ConnectionMultiplexer.ConnectAsync(endpoint);
                return await connecting.WaitAsync(ct);
            },
            owned: true
        );

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private (RespServer Server, Mock<IDatabase> Database) CreateServer()
    {
        var database = Mocks.Create<IDatabase>();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object)
            .Verifiable();
        var connection = new RespConnection(
            _ => ValueTask.FromResult(multiplexer.Object),
            owned: false
        );

        return (new RespServer(connection), database);
    }

    #region ParseInfo Tests

    [Fact]
    public void ParseInfo_ServerSection_ParsesFields()
    {
        var fields = RespServer.ParseInfo(
            "# Server\r\nserver_name:valkey\r\nvalkey_version:9.2.0\r\n"
        );

        Assert.Equal(2, fields.Count);
        Assert.Equal("valkey", fields["server_name"]);
        Assert.Equal("9.2.0", fields["valkey_version"]);
    }

    #endregion

    #region EvaluateAsync Tests

    [Fact]
    public async Task EvaluateAsync_ServerRefusesWithNoperm_ThrowsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var failure = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "NOPERM this user has no permissions to run the 'eval' command"
        );
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .ThrowsAsync(failure)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.EvaluateAsync(RespScript.Probe, [], [], cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.AccessDenied, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task EvaluateAsync_ScriptError_ThrowsOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var failure = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "ERR Error running script"
        );
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .ThrowsAsync(failure)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.EvaluateAsync(RespScript.Probe, [], [], cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task EvaluateAsync_CanceledWhileWaiting_ThrowsOperationCanceled()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var (server, database) = CreateServer();
        var reply = new TaskCompletionSource<RedisResult>();
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .Returns(reply.Task)
            .Verifiable();

        var evaluating = server
            .EvaluateAsync(RespScript.Probe, [], [], cancellation.Token)
            .AsTask();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await evaluating);
    }

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

    [Fact]
    public async Task ExecuteAsync_ServerError_ThrowsOutcomeKnown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var failure = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "ERR unknown failure"
        );
        database
            .Setup(x => x.ExecuteAsync("GET", It.IsAny<object[]>()))
            .ThrowsAsync(failure)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync("GET", ["key"], cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
    }

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
