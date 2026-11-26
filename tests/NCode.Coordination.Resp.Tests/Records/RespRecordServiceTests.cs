// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Records.Tests;

public sealed class RespRecordServiceTests(RespServers servers) : IDisposable
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

    private static async Task<ConnectionMultiplexer> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken
    ) => await ConnectionMultiplexer.ConnectAsync(endpoint).WaitAsync(cancellationToken);

    private static RespRecordService CreateService(
        IConnectionMultiplexer multiplexer,
        RespProfile profile
    )
    {
        var connection = new RespConnection(_ => ValueTask.FromResult(multiplexer), owned: false);

        return new RespRecordService(new RespRecordTable(new RespServer(connection), profile));
    }

    private static Keyspace CreateKeyspace() =>
        new("resp-tests", KeySegment.FromGuid(Guid.NewGuid()));

    private static RedisChannel WatchChannel(Keyspace keyspace, RespProfile profile)
    {
        var name = RespRecordTable.WatchChannel(keyspace);

        return profile.ShardedChannels ? RedisChannel.Sharded(name) : RedisChannel.Literal(name);
    }

    private static async Task<string> ReadMessageAsync(
        ChannelMessageQueue queue,
        CancellationToken cancellationToken
    )
    {
        var message = await queue
            .ReadAsync(cancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        var text = (string?)message.Message;
        Assert.NotNull(text);

        return text;
    }

    private static RecordLifetime ForMilliseconds(int milliseconds) =>
        RecordLifetime.ForTimeToLive(new TimeToLive(TimeSpan.FromMilliseconds(milliseconds)));

    private static async Task AssertCreatePublishesTheWriteAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var service = CreateService(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var key = keyspace.CreateKey("cfg", 7);
        var queue = await multiplexer
            .GetSubscriber()
            .SubscribeAsync(WatchChannel(keyspace, profile));

        var result = await service.CreateAsync(
            key,
            "v"u8.ToArray(),
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        var message = await ReadMessageAsync(queue, cancellationToken);
        await queue.UnsubscribeAsync();

        Assert.True(result.IsWritten);
        Assert.Equal($"{result.Revision.Value}\nw{RespKeys.Name(key)}", message);
    }

    private static async Task AssertReadDeletesAnExpiredRecordAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var service = CreateService(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var a = keyspace.CreateKey("a");
        var created = await service.CreateAsync(
            a,
            "v"u8.ToArray(),
            ForMilliseconds(200),
            null,
            cancellationToken
        );
        Assert.True(created.IsWritten);
        var queue = await multiplexer
            .GetSubscriber()
            .SubscribeAsync(WatchChannel(keyspace, profile));

        await Task.Delay(400, cancellationToken);
        var read = await service.ReadAsync(keyspace.CreateKey("b"), null, cancellationToken);
        var message = await ReadMessageAsync(queue, cancellationToken);
        await queue.UnsubscribeAsync();
        var recordsKey = RespRecordTable.Keys(keyspace)[1];
        var exists = await multiplexer.GetDatabase().KeyExistsAsync(recordsKey);

        Assert.Null(read);
        Assert.Equal($"{created.Revision.Value + 1}\nd{RespKeys.Name(a)}", message);
        Assert.False(exists);
    }

    private static async Task AssertCreateKeepsTheTableAnHourPastTheLastDeadlineAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var service = CreateService(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var database = multiplexer.GetDatabase();
        var recordsKey = RespRecordTable.Keys(keyspace)[1];

        await service.CreateAsync(
            keyspace.CreateKey("a"),
            "v"u8.ToArray(),
            ForMilliseconds(30_000),
            null,
            cancellationToken
        );
        var withDeadline = await database.KeyTimeToLiveAsync(recordsKey);
        await service.CreateAsync(
            keyspace.CreateKey("b"),
            "v"u8.ToArray(),
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        var withPersistent = await database.KeyTimeToLiveAsync(recordsKey);

        Assert.NotNull(withDeadline);
        Assert.InRange(withDeadline.Value, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(61));
        Assert.Null(withPersistent);
    }

    private static async Task AssertListPagesThroughKeysOfTheLongestSegmentsAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var service = CreateService(multiplexer, profile);
        var keyspace = CreateKeyspace();
        var texts = Enumerable
            .Range(0, 15)
            .Select(_ => KeySegment.FromString(new string('\u00e9', 256)))
            .ToList();
        var keys = Enumerable
            .Range(0, 3)
            .Select(number => keyspace.CreateKey([.. texts, KeySegment.FromInt64(number)]))
            .ToList();
        foreach (var key in keys)
        {
            var result = await service.CreateAsync(
                key,
                "v"u8.ToArray(),
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );
            Assert.True(result.IsWritten);
        }
        var prefix = keyspace.CreateKey(texts[0]);

        var seen = new List<CoordinationKey>();
        string? token = null;
        var pages = 0;
        do
        {
            var page = await service.ListAsync(
                prefix,
                new RecordPageRequest(1) { ContinuationToken = token },
                null,
                cancellationToken
            );
            seen.AddRange(page.Records.Select(record => record.Key));
            token = page.ContinuationToken;
            pages++;
        } while (token is not null && pages < 10);

        Assert.Null(token);
        Assert.Equal(3, seen.Count);
        Assert.Equal(keys.Count, seen.Distinct().Count());
        Assert.All(keys, key => Assert.Contains(key, seen));
    }

    private static async Task AssertReadDeletesAtMost64ExpiredRecordsAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var service = CreateService(multiplexer, profile);
        var keyspace = CreateKeyspace();
        for (var index = 0; index < 65; index++)
        {
            var result = await service.CreateAsync(
                keyspace.CreateKey("e", index),
                "v"u8.ToArray(),
                ForMilliseconds(300),
                null,
                cancellationToken
            );
            Assert.True(result.IsWritten);
        }
        var queue = await multiplexer
            .GetSubscriber()
            .SubscribeAsync(WatchChannel(keyspace, profile));

        await Task.Delay(500, cancellationToken);
        var other = keyspace.CreateKey("other");
        await service.ReadAsync(other, null, cancellationToken);
        var first = await ReadMessageAsync(queue, cancellationToken);
        await service.ReadAsync(other, null, cancellationToken);
        var second = await ReadMessageAsync(queue, cancellationToken);
        await queue.UnsubscribeAsync();

        Assert.Equal(65, first.Split('\n').Length);
        Assert.Equal(2, second.Split('\n').Length);
    }

    #region CreateAsync Tests

    [RespServerFact]
    public Task CreateAsync_Valkey_PublishesTheWriteOnTheWatchChannel() =>
        AssertCreatePublishesTheWriteAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task CreateAsync_Garnet_PublishesTheWriteOnTheWatchChannel() =>
        AssertCreatePublishesTheWriteAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task CreateAsync_Valkey_KeepsTheTableAnHourPastTheLastDeadline() =>
        AssertCreateKeepsTheTableAnHourPastTheLastDeadlineAsync(
            servers.Valkey,
            RespProfiles.Valkey
        );

    [RespServerFact]
    public Task CreateAsync_Garnet_KeepsTheTableAnHourPastTheLastDeadline() =>
        AssertCreateKeepsTheTableAnHourPastTheLastDeadlineAsync(
            servers.Garnet,
            RespProfiles.Garnet
        );

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

    #region ReadAsync Tests

    [RespServerFact]
    public Task ReadAsync_Valkey_DeletesAnExpiredRecordAndPublishesTheDelete() =>
        AssertReadDeletesAnExpiredRecordAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task ReadAsync_Garnet_DeletesAnExpiredRecordAndPublishesTheDelete() =>
        AssertReadDeletesAnExpiredRecordAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task ReadAsync_Valkey_DeletesAtMost64ExpiredRecordsAtOnce() =>
        AssertReadDeletesAtMost64ExpiredRecordsAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task ReadAsync_Garnet_DeletesAtMost64ExpiredRecordsAtOnce() =>
        AssertReadDeletesAtMost64ExpiredRecordsAsync(servers.Garnet, RespProfiles.Garnet);

    #endregion

    #region ListAsync Tests

    [RespServerFact]
    public Task ListAsync_Valkey_PagesThroughKeysOfTheLongestSegments() =>
        AssertListPagesThroughKeysOfTheLongestSegmentsAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task ListAsync_Garnet_PagesThroughKeysOfTheLongestSegments() =>
        AssertListPagesThroughKeysOfTheLongestSegmentsAsync(servers.Garnet, RespProfiles.Garnet);

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
