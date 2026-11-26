// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespServerTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private (RespServer Server, Mock<IDatabase> Database) CreateServer()
    {
        var database = Mocks.Create<IDatabase>();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var connection = new RespConnection(_ => Task.FromResult(multiplexer.Object), owned: false);

        return (new RespServer(connection), database);
    }

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
            .Returns(Task.FromException<RedisResult>(failure))
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
            .Returns(Task.FromException<RedisResult>(failure))
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.EvaluateAsync(RespScript.Probe, [], [], cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
    }

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
            .Returns(Task.FromException<RedisResult>(failure))
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await server.ExecuteAsync("GET", ["key"], cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
    }
}
