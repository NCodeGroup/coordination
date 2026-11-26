// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Sets.Tests;

public sealed class RespSetServiceTests : IDisposable
{
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

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_EncodedMember_ReturnsTheSegment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var key = new Keyspace("prod").CreateKey("k");
        var status = RedisResult.Create((RedisValue)0, ResultType.Integer);
        var member = RedisResult.Create((RedisValue)"ta");
        var reply = RedisResult.Create([status, member]);
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .ReturnsAsync(reply)
            .Verifiable();

        var members = await new RespSetService(server).ListAsync(key, null, cancellationToken);

        var segment = Assert.Single(members);
        Assert.Equal(KeySegment.FromString("a"), segment);
    }

    [Fact]
    public async Task ListAsync_MemberTheProviderDidNotWrite_ThrowsUnavailableWithOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var key = new Keyspace("prod").CreateKey("k");
        var status = RedisResult.Create((RedisValue)0, ResultType.Integer);
        var member = RedisResult.Create((RedisValue)"x1");
        var reply = RedisResult.Create([status, member]);
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .ReturnsAsync(reply)
            .Verifiable();
        var service = new RespSetService(server);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ListAsync(key, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    #endregion
}
