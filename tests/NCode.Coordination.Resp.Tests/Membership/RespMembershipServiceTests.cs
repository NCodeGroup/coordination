// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Membership.Tests;

public sealed class RespMembershipServiceTests : IDisposable
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

    #region WatchAsync Tests

    [Fact]
    public async Task WatchAsync_WhenTheRevisionHasNotMoved_ListsTheGroupOnlyOnce()
    {
        var (server, database) = CreateServer();
        var table = new RespRecordTable(server, RespProfiles.Valkey);
        var service = new RespMembershipService(table);
        var group = new Keyspace("resp-tests", KeySegment.FromGuid(Guid.NewGuid())).CreateKey(
            "group"
        );
        var reply = RedisResult.Create([
            RedisResult.Create((RedisValue)0, ResultType.Integer),
            RedisResult.Create((RedisValue)"5"),
        ]);
        var listText = RespScript.MemberList.Text;
        var revisionText = RespScript.KeyspaceRevision.Text;
        var polls = 0;
        var polled = new TaskCompletionSource();
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    listText,
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .ReturnsAsync(reply)
            .Verifiable();
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    revisionText,
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .Callback(() =>
            {
                polls++;
                if (polls == 2)
                {
                    polled.TrySetResult();
                }
            })
            .ReturnsAsync(reply)
            .Verifiable();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var enumerator = service
            .WatchAsync(group, null, source.Token)
            .GetAsyncEnumerator(source.Token);

        Assert.True(await enumerator.MoveNextAsync());
        var next = enumerator.MoveNextAsync().AsTask();
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
        await enumerator.DisposeAsync();

        database.Verify(
            x =>
                x.ScriptEvaluateAsync(
                    listText,
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                ),
            Times.Once()
        );
    }

    #endregion
}
