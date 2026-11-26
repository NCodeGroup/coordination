// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records.Tests;

public sealed class RespRecordServiceTests : IDisposable
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

    private static RespServer CreateUnreachableServer()
    {
        var connection = new RespConnection(
            _ =>
                ValueTask.FromException<IConnectionMultiplexer>(
                    new InvalidOperationException("The test reaches no server.")
                ),
            owned: false
        );

        return new RespServer(connection);
    }

    private static RespRecordService CreateService(RespServer server) =>
        new(new RespRecordTable(server, RespProfiles.Garnet));

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_SessionLifetime_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var service = CreateService(CreateUnreachableServer());
        var lifetime = RecordLifetime.ForSession(new SessionId(keyspace, "s1"));

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CreateAsync(
                keyspace.CreateKey("a"),
                "v"u8.ToArray(),
                lifetime,
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_TokenOfAnotherPrefix_ThrowsArgumentException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var service = CreateService(CreateUnreachableServer());
        var page = new RecordPageRequest(10)
        {
            ContinuationToken = RespKeys.Member(keyspace.CreateKey("b")),
        };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ListAsync(keyspace.CreateKey("a"), page, null, cancellationToken)
        );
    }

    [Fact]
    public async Task ListAsync_Reply_ReturnsTheRecordsAndTheToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var keyspace = new Keyspace("prod");
        var reply = RedisResult.Create([
            RedisResult.Create((RedisValue)0, ResultType.Integer),
            RedisResult.Create((RedisValue)"7"),
            RedisResult.Create((RedisValue)"tok"),
            RedisResult.Create((RedisValue)"0:ta/tb"),
            RedisResult.Create((RedisValue)"5"),
            RedisResult.Create((RedisValue)"v"),
        ]);
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
        var service = CreateService(server);

        var result = await service.ListAsync(
            keyspace.CreateKey("a"),
            new RecordPageRequest(10),
            null,
            cancellationToken
        );

        Assert.Equal(new Revision(7), result.Revision);
        var record = Assert.Single(result.Records);
        Assert.Equal(keyspace.CreateKey("a", "b"), record.Key);
        Assert.Equal(new Revision(5), record.Revision);
        Assert.Equal("v"u8.ToArray(), record.Value.ToArray());
        Assert.Equal("tok", result.ContinuationToken);
    }

    [Fact]
    public async Task ListAsync_NameTheProviderDidNotWrite_ThrowsUnavailableWithOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var keyspace = new Keyspace("prod");
        var reply = RedisResult.Create([
            RedisResult.Create((RedisValue)0, ResultType.Integer),
            RedisResult.Create((RedisValue)"7"),
            RedisResult.Create((RedisValue)"tok"),
            RedisResult.Create((RedisValue)"x:ta"),
            RedisResult.Create((RedisValue)"5"),
            RedisResult.Create((RedisValue)"v"),
        ]);
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
        var service = CreateService(server);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ListAsync(
                keyspace.CreateKey("a"),
                new RecordPageRequest(10),
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    #endregion
}
