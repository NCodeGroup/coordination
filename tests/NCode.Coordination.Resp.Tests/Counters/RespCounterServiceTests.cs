// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Moq;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Counters.Tests;

public sealed class RespCounterServiceTests(RespServers servers) : IDisposable
{
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

    #region LargestCount Tests

    [Fact]
    public void LargestCount_Always_IsTheScriptsLargestCount()
    {
        var largest = RespCounterService.LargestCount.ToString(CultureInfo.InvariantCulture);
        var expected = $"local largest = {largest}";
        var text = RespScript.CounterIncrement.Text;

        Assert.Contains(expected, text);
    }

    #endregion

    #region IncrementAsync Tests

    [RespServerFact]
    public async Task IncrementAsync_ValkeyAtTheLargestCount_RefusesTheNextIncrement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = CreateConnection(servers.Valkey);
        var server = new RespServer(connection);
        var service = new RespCounterService(server);
        var keyspace = new Keyspace("resp-tests");
        var name = Guid.NewGuid().ToString("N");
        var key = keyspace.CreateKey(name);
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);

        var largest = await service.IncrementAsync(
            key,
            RespCounterService.LargestCount,
            timeToLive,
            null,
            cancellationToken
        );
        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementAsync(key, 1, timeToLive, null, cancellationToken)
        );
        var count = await service.ReadAsync(key, null, cancellationToken);

        Assert.Equal(RespCounterService.LargestCount, largest);
        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(RespCounterService.LargestCount, count);
    }

    [RespServerFact]
    public async Task IncrementAsync_GarnetAtTheLargestCount_RefusesTheNextIncrement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = CreateConnection(servers.Garnet);
        var server = new RespServer(connection);
        var service = new RespCounterService(server);
        var keyspace = new Keyspace("resp-tests");
        var name = Guid.NewGuid().ToString("N");
        var key = keyspace.CreateKey(name);
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);

        var largest = await service.IncrementAsync(
            key,
            RespCounterService.LargestCount,
            timeToLive,
            null,
            cancellationToken
        );
        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementAsync(key, 1, timeToLive, null, cancellationToken)
        );
        var count = await service.ReadAsync(key, null, cancellationToken);

        Assert.Equal(RespCounterService.LargestCount, largest);
        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(RespCounterService.LargestCount, count);
    }

    #endregion

    #region IncrementBatchAsync Tests

    [Fact]
    public async Task IncrementBatchAsync_ScriptRefusesThePassedLargestCount_ThrowsLimitExceededWithOutcomeKnown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var keyspace = new Keyspace("resp-tests");
        var name = Guid.NewGuid().ToString("N");
        var key = keyspace.CreateKey(name);
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var increment = new CounterIncrement(key, 1, 10, timeToLive);
        var status = RedisResult.Create((RedisValue)1, ResultType.Integer);
        var reply = RedisResult.Create([status]);
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
        var service = new RespCounterService(server);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementBatchAsync([increment], null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task IncrementBatchAsync_ExceededIndexOutOfRange_ThrowsUnavailableWithOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var keyspace = new Keyspace("resp-tests");
        var name = Guid.NewGuid().ToString("N");
        var key = keyspace.CreateKey(name);
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var increment = new CounterIncrement(key, 1, 10, timeToLive);
        var status = RedisResult.Create((RedisValue)0, ResultType.Integer);
        var exceeded = RedisResult.Create((RedisValue)3, ResultType.Integer);
        var count = RedisResult.Create((RedisValue)7, ResultType.Integer);
        var reply = RedisResult.Create([status, exceeded, count]);
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
        var service = new RespCounterService(server);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementBatchAsync([increment], null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task IncrementBatchAsync_NoCounts_ThrowsUnavailableWithOutcomeUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var keyspace = new Keyspace("resp-tests");
        var name = Guid.NewGuid().ToString("N");
        var key = keyspace.CreateKey(name);
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var increment = new CounterIncrement(key, 1, 10, timeToLive);
        var status = RedisResult.Create((RedisValue)0, ResultType.Integer);
        var exceeded = RedisResult.Create((RedisValue)0, ResultType.Integer);
        var reply = RedisResult.Create([status, exceeded]);
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
        var service = new RespCounterService(server);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementBatchAsync([increment], null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    [RespServerFact]
    public async Task IncrementBatchAsync_ValkeySecondIncrementPassesTheLargestCount_AppliesNone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = CreateConnection(servers.Valkey);
        var server = new RespServer(connection);
        var service = new RespCounterService(server);
        var keyspace = new Keyspace("resp-tests");
        var name = Guid.NewGuid().ToString("N");
        var key = keyspace.CreateKey(name);
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var first = new CounterIncrement(key, 5, long.MaxValue, timeToLive);
        var second = new CounterIncrement(
            key,
            RespCounterService.LargestCount,
            long.MaxValue,
            timeToLive
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementBatchAsync([first, second], null, cancellationToken)
        );
        var count = await service.ReadAsync(key, null, cancellationToken);

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(0, count);
    }

    [RespServerFact]
    public async Task IncrementBatchAsync_GarnetSecondIncrementPassesTheLargestCount_AppliesNone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = CreateConnection(servers.Garnet);
        var server = new RespServer(connection);
        var service = new RespCounterService(server);
        var keyspace = new Keyspace("resp-tests");
        var name = Guid.NewGuid().ToString("N");
        var key = keyspace.CreateKey(name);
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var first = new CounterIncrement(key, 5, long.MaxValue, timeToLive);
        var second = new CounterIncrement(
            key,
            RespCounterService.LargestCount,
            long.MaxValue,
            timeToLive
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementBatchAsync([first, second], null, cancellationToken)
        );
        var count = await service.ReadAsync(key, null, cancellationToken);

        Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(0, count);
    }

    #endregion
}
