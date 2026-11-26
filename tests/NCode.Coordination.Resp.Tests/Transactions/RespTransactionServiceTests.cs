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

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_SessionInTheFailureBranch_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var server = CreateUnreachableServer();
        var table = new RespRecordTable(server, RespProfiles.Garnet);
        var service = new RespTransactionService(table);
        var key = keyspace.CreateKey("k");
        var other = keyspace.CreateKey("other");
        var value = "v"u8.ToArray();
        var sessionId = new SessionId(keyspace, "s1");
        var lifetime = RecordLifetime.ForSession(sessionId);
        var put = RecordWrite.Put(other, value, lifetime);
        var delete = RecordWrite.Delete(key);
        var transaction = new RecordTransaction([], [delete]) { OnFailure = [put] };

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
        var status = RedisResult.Create((RedisValue)1, ResultType.Integer);
        var revisionField = RedisResult.Create((RedisValue)"9");
        var reply = RedisResult.Create([status, revisionField]);
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
        var service = new RespTransactionService(table);
        var expectedRevision = new Revision(5);
        var expectedValue = "x"u8.ToArray();
        var absent = RecordCondition.Absent(a);
        var revisionEquals = RecordCondition.RevisionEquals(b, expectedRevision);
        var valueEquals = RecordCondition.ValueEquals(c, expectedValue);
        var value = "one"u8.ToArray();
        var duration = TimeSpan.FromSeconds(2);
        var timeToLive = new TimeToLive(duration);
        var lifetime = RecordLifetime.ForTimeToLive(timeToLive);
        var put = RecordWrite.Put(a, value, lifetime);
        var delete = RecordWrite.Delete(b);
        var transaction = new RecordTransaction([absent, revisionEquals, valueEquals], [put])
        {
            OnFailure = [delete],
        };
        var memberA = RespKeys.Member(a);
        var memberB = RespKeys.Member(b);
        var memberC = RespKeys.Member(c);
        var nameA = RespKeys.Name(a);
        var nameB = RespKeys.Name(b);
        string[] expected =
        [
            "{prod}:watch",
            "1",
            "3600000",
            "3",
            "a",
            memberA,
            "",
            "r",
            memberB,
            "5",
            "v",
            memberC,
            "x",
            "1",
            "p",
            memberA,
            nameA,
            "one",
            "2000",
            "1",
            "d",
            memberB,
            nameB,
            "",
            "",
        ];

        var result = await service.ExecuteAsync(transaction, null, cancellationToken);

        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Equal(new Revision(9), result.Revision);
        Assert.NotNull(values);
        var actual = values.Select(item => item.ToString());
        Assert.Equal(expected, actual);
    }

    #endregion
}
