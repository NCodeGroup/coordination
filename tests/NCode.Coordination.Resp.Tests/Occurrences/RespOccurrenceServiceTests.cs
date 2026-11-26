// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Occurrences.Tests;

public sealed class RespOccurrenceServiceTests : IDisposable
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

    #region TryRecordAsync Tests

    [Fact]
    public async Task TryRecordAsync_ServerAnswersOk_ReturnsRecorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var key = new Keyspace("prod").CreateKey("k");
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(5));
        var reply = RedisResult.Create((RedisValue)"OK", ResultType.SimpleString);
        database
            .Setup(x => x.ExecuteAsync("SET", It.IsAny<object[]>()))
            .ReturnsAsync(reply)
            .Verifiable();

        var status = await new RespOccurrenceService(server).TryRecordAsync(
            key,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.Equal(OccurrenceStatus.Recorded, status);
    }

    [Fact]
    public async Task TryRecordAsync_ServerAnswersNull_ReturnsDuplicate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var key = new Keyspace("prod").CreateKey("k");
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(5));
        var reply = RedisResult.Create(RedisValue.Null, ResultType.BulkString);
        database
            .Setup(x => x.ExecuteAsync("SET", It.IsAny<object[]>()))
            .ReturnsAsync(reply)
            .Verifiable();

        var status = await new RespOccurrenceService(server).TryRecordAsync(
            key,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.Equal(OccurrenceStatus.Duplicate, status);
    }

    [Fact]
    public async Task TryRecordAsync_ServerAnswersAnotherStatus_ThrowsUnavailableWithOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var key = new Keyspace("prod").CreateKey("k");
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(5));
        var reply = RedisResult.Create((RedisValue)"QUEUED", ResultType.SimpleString);
        database
            .Setup(x => x.ExecuteAsync("SET", It.IsAny<object[]>()))
            .ReturnsAsync(reply)
            .Verifiable();
        var service = new RespOccurrenceService(server);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryRecordAsync(key, timeToLive, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    #endregion
}
