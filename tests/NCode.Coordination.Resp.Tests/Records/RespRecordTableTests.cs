// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records.Tests;

public sealed class RespRecordTableTests : IDisposable
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

    #region EvaluateAsync Tests

    [Fact]
    public async Task EvaluateAsync_Garnet_PassesTheTableKeysThenThePlainChannel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        RedisKey[]? keys = null;
        RedisValue[]? values = null;
        var reply = RedisResult.Create((RedisValue)0, ResultType.Integer);
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .Callback<string, RedisKey[]?, RedisValue[]?, CommandFlags>(
                (_, k, v, _) =>
                {
                    keys = k;
                    values = v;
                }
            )
            .ReturnsAsync(reply)
            .Verifiable();
        var table = new RespRecordTable(server, RespProfiles.Garnet);
        var keyspace = new Keyspace("prod");

        await table.EvaluateAsync(RespScript.RecordCreate, keyspace, ["m"], cancellationToken);

        Assert.NotNull(keys);
        var names = keys.Select(key => key.ToString());
        Assert.Equal(
            [
                "{prod}:revision",
                "{prod}:records",
                "{prod}:record-index",
                "{prod}:record-deadlines",
                "{prod}:sessions",
                "{prod}:session-deadlines",
                "{prod}:session-bindings",
                "{prod}:grants",
                "{prod}:grant-deadlines",
                "{prod}:fencing",
                "{prod}:members",
                "{prod}:member-fields",
            ],
            names
        );
        Assert.NotNull(values);
        Assert.Equal(4, values.Length);
        Assert.Equal("{prod}:watch", (string?)values[0]);
        Assert.Equal("0", (string?)values[1]);
        Assert.Equal(3_600_000L, (long)values[2]);
        Assert.Equal("m", (string?)values[3]);
    }

    [Fact]
    public async Task EvaluateAsync_Valkey_PublishesOnTheShardedChannel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        RedisValue[]? values = null;
        var reply = RedisResult.Create((RedisValue)0, ResultType.Integer);
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .Callback<string, RedisKey[]?, RedisValue[]?, CommandFlags>((_, _, v, _) => values = v)
            .ReturnsAsync(reply)
            .Verifiable();
        var table = new RespRecordTable(server, RespProfiles.Valkey);
        var keyspace = new Keyspace("prod");

        await table.EvaluateAsync(RespScript.RecordCreate, keyspace, ["m"], cancellationToken);

        Assert.NotNull(values);
        Assert.Equal("1", (string?)values[1]);
    }

    #endregion

    #region TimeToLiveOf Tests

    [Fact]
    public void TimeToLiveOf_Persistent_ReturnsZero()
    {
        var milliseconds = RespRecordTable.TimeToLiveOf(RecordLifetime.Persistent);

        Assert.Equal(0, milliseconds);
    }

    [Fact]
    public void TimeToLiveOf_PartOfAMillisecond_RoundsUp()
    {
        var duration = TimeSpan.FromTicks(15_001);
        var timeToLive = new TimeToLive(duration);
        var lifetime = RecordLifetime.ForTimeToLive(timeToLive);

        var milliseconds = RespRecordTable.TimeToLiveOf(lifetime);

        Assert.Equal(2, milliseconds);
    }

    [Fact]
    public void TimeToLiveOf_Session_ReturnsZero()
    {
        var keyspace = new Keyspace("prod");
        var sessionId = new SessionId(keyspace, "s1");
        var lifetime = RecordLifetime.ForSession(sessionId);

        var milliseconds = RespRecordTable.TimeToLiveOf(lifetime);

        Assert.Equal(0, milliseconds);
    }

    #endregion

    #region SessionOf Tests

    [Fact]
    public void SessionOf_Session_ReturnsItsToken()
    {
        var keyspace = new Keyspace("prod");
        var sessionId = new SessionId(keyspace, "s1");
        var lifetime = RecordLifetime.ForSession(sessionId);

        var token = RespRecordTable.SessionOf(lifetime);

        Assert.Equal("s1", token);
    }

    [Fact]
    public void SessionOf_Persistent_ReturnsEmpty()
    {
        var token = RespRecordTable.SessionOf(RecordLifetime.Persistent);

        Assert.Equal("", token);
    }

    #endregion

    #region ReadRevision Tests

    [Fact]
    public void ReadRevision_Zero_ThrowsUnavailable()
    {
        var field = RedisResult.Create((RedisValue)0, ResultType.Integer);
        var reply = RedisResult.Create([field]);

        var exception = Assert.Throws<CoordinationException>(() =>
            RespRecordTable.ReadRevision(field, reply)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    #endregion

    #region ReadValue Tests

    [Fact]
    public void ReadValue_Null_ThrowsUnavailable()
    {
        var field = RedisResult.Create(RedisValue.Null, ResultType.BulkString);
        var reply = RedisResult.Create([field]);
        Assert.True(field.IsNull);

        var exception = Assert.Throws<CoordinationException>(() =>
            RespRecordTable.ReadValue(field, reply)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    #endregion
}
