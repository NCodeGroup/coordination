// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Transactions.Tests;

public sealed class RespTransactionServiceTests : IDisposable
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

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_SessionInTheFailureBranch_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var service = new RespTransactionService(
            new RespRecordTable(CreateUnreachableServer(), RespProfiles.Garnet)
        );
        var key = keyspace.CreateKey("k");
        var transaction = new RecordTransaction([], [RecordWrite.Delete(key)])
        {
            OnFailure =
            [
                RecordWrite.Put(
                    key.Keyspace.CreateKey("other"),
                    "v"u8.ToArray(),
                    RecordLifetime.ForSession(new SessionId(keyspace, "s1"))
                ),
            ],
        };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ExecuteAsync(transaction, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task ExecuteAsync_Transaction_PassesItsConditionsThenBothBranches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (server, database) = CreateServer();
        var keyspace = new Keyspace("prod");
        var a = keyspace.CreateKey("a");
        var b = keyspace.CreateKey("b");
        var c = keyspace.CreateKey("c");
        RedisValue[]? values = null;
        var reply = RedisResult.Create([
            RedisResult.Create((RedisValue)1, ResultType.Integer),
            RedisResult.Create((RedisValue)"9"),
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
            .Callback<string, RedisKey[]?, RedisValue[]?, CommandFlags>((_, _, v, _) => values = v)
            .ReturnsAsync(reply)
            .Verifiable();
        var service = new RespTransactionService(new RespRecordTable(server, RespProfiles.Valkey));
        var transaction = new RecordTransaction(
            [
                RecordCondition.Absent(a),
                RecordCondition.RevisionEquals(b, new Revision(5)),
                RecordCondition.ValueEquals(c, "x"u8.ToArray()),
            ],
            [
                RecordWrite.Put(
                    a,
                    "one"u8.ToArray(),
                    RecordLifetime.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(2)))
                ),
            ]
        )
        {
            OnFailure = [RecordWrite.Delete(b)],
        };

        var result = await service.ExecuteAsync(transaction, null, cancellationToken);

        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Equal(new Revision(9), result.Revision);
        Assert.NotNull(values);
        Assert.Equal(
            [
                "{prod}:watch",
                "1",
                "3600000",
                "3",
                "a",
                RespKeys.Member(a),
                "",
                "r",
                RespKeys.Member(b),
                "5",
                "v",
                RespKeys.Member(c),
                "x",
                "1",
                "p",
                RespKeys.Member(a),
                RespKeys.Name(a),
                "one",
                "2000",
                "1",
                "d",
                RespKeys.Member(b),
                RespKeys.Name(b),
                "",
                "",
            ],
            values.Select(value => value.ToString())
        );
    }

    #endregion
}
