// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Notifications.Tests;

public sealed class RespNotificationServiceTests(RespServers servers) : IDisposable
{
    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace("resp-tests", segment);
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        var connecting = ConnectionMultiplexer.ConnectAsync(endpoint);

        return await connecting.WaitAsync(cancellationToken);
    }

    private static (RespNotificationService Service, RespSubscriptions Subscriptions) CreateService(
        IConnectionMultiplexer multiplexer,
        RespProfile profile
    )
    {
        var connection = new RespConnection(_ => ValueTask.FromResult(multiplexer), owned: false);

        var server = new RespServer(connection);
        var checkInterval = TimeSpan.FromMilliseconds(200);
        var schedule = new RespCheckSchedule(checkInterval, TimeProvider.System);
        var subscriptions = new RespSubscriptions(connection, profile, schedule);

        return (new RespNotificationService(server, profile, subscriptions), subscriptions);
    }

    private static async Task<Notification> NextAsync(
        IAsyncEnumerator<Notification> enumerator,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(15);
        var moving = enumerator.MoveNextAsync().AsTask();
        var moved = await moving.WaitAsync(timeout, cancellationToken);
        Assert.True(moved);

        return enumerator.Current;
    }

    private static async Task AssertSequenceMovingWithoutAMessageResyncsAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (service, _) = CreateService(multiplexer, profile);
        var topic = CreateKeyspace().CreateKey("t");
        var key = RespKeys.ForKey(topic, RespNotificationService.Kind);
        var stream = service.SubscribeAsync(topic, null, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        var start = await NextAsync(enumerator, cancellationToken);
        Assert.True(start.IsResync);
        var payload = "a"u8.ToArray();
        await service.PublishAsync(topic, payload, null, cancellationToken);
        var first = await NextAsync(enumerator, cancellationToken);
        Assert.Equal(NotificationKind.Message, first.Kind);
        Assert.Equal(1, first.Sequence);
        await multiplexer.GetDatabase().StringIncrementAsync(key);
        var resync = await NextAsync(enumerator, cancellationToken);

        Assert.True(resync.IsResync);
        Assert.Equal(1, resync.Sequence);
    }

    private static async Task AssertMessageSkippingANumberResyncsAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (service, subscriptions) = CreateService(multiplexer, profile);
        var topic = CreateKeyspace().CreateKey("t");
        var key = RespKeys.ForKey(topic, RespNotificationService.Kind);
        var stream = service.SubscribeAsync(topic, null, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        var start = await NextAsync(enumerator, cancellationToken);
        Assert.True(start.IsResync);
        var payload = "a"u8.ToArray();
        await service.PublishAsync(topic, payload, null, cancellationToken);
        var first = await NextAsync(enumerator, cancellationToken);
        Assert.Equal(1, first.Sequence);
        var name = key.ToString();
        var channel = subscriptions.Channel(name);
        await multiplexer.GetSubscriber().PublishAsync(channel, "3\nb");
        var resync = await NextAsync(enumerator, cancellationToken);

        Assert.True(resync.IsResync);
        Assert.Equal(1, resync.Sequence);
    }

    private static async Task AssertMessageWithoutAPayloadResyncsAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (service, subscriptions) = CreateService(multiplexer, profile);
        var topic = CreateKeyspace().CreateKey("t");
        var key = RespKeys.ForKey(topic, RespNotificationService.Kind);
        var name = key.ToString();
        var channel = subscriptions.Channel(name);
        var database = multiplexer.GetDatabase();
        var subscriber = multiplexer.GetSubscriber();
        var stream = service.SubscribeAsync(topic, null, cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        var start = await NextAsync(enumerator, cancellationToken);
        Assert.True(start.IsResync);
        Assert.Equal(0, start.Sequence);
        var set = await database.StringSetAsync(key, 1);
        Assert.True(set);
        await subscriber.PublishAsync(channel, "1");
        var resync = await NextAsync(enumerator, cancellationToken);

        Assert.True(resync.IsResync);
        Assert.Equal(0, resync.Sequence);
    }

    private static async Task AssertAnotherSubscriptionEndingKeepsDeliveringAsync(
        string endpoint,
        RespProfile profile
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var multiplexer = await ConnectAsync(endpoint, cancellationToken);
        var (service, _) = CreateService(multiplexer, profile);
        var topic = CreateKeyspace().CreateKey("t");
        var firstStream = service.SubscribeAsync(topic, null, cancellationToken);
        var secondStream = service.SubscribeAsync(topic, null, cancellationToken);
        await using var first = firstStream.GetAsyncEnumerator(cancellationToken);
        await using var second = secondStream.GetAsyncEnumerator(cancellationToken);

        var firstStart = await NextAsync(first, cancellationToken);
        var secondStart = await NextAsync(second, cancellationToken);
        Assert.True(firstStart.IsResync);
        Assert.True(secondStart.IsResync);
        await second.DisposeAsync();
        var payload = "a"u8.ToArray();
        await service.PublishAsync(topic, payload, null, cancellationToken);
        var next = await NextAsync(first, cancellationToken);

        Assert.Equal(NotificationKind.Message, next.Kind);
        Assert.Equal(1, next.Sequence);
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region TryReadPayload Tests

    [Fact]
    public void TryReadPayload_WhenTheMessageHasALineFeed_ReturnsTheBytesAfterIt()
    {
        var expected = "pay\nload"u8.ToArray();
        var found = RespNotificationService.TryReadPayload("12\npay\nload", out var payload);

        Assert.True(found);
        Assert.Equal(expected, payload);
    }

    [Fact]
    public void TryReadPayload_WhenTheMessageHasNoLineFeed_ReturnsFalse()
    {
        var found = RespNotificationService.TryReadPayload("12", out _);

        Assert.False(found);
    }

    [Fact]
    public void TryReadPayload_WhenTheMessageIsNull_ReturnsFalse()
    {
        var found = RespNotificationService.TryReadPayload(RedisValue.Null, out var payload);

        Assert.False(found);
        Assert.Empty(payload);
    }

    [Fact]
    public void TryReadPayload_WhenThePayloadIsAtTheLimit_ReturnsIt()
    {
        var expected = new byte[CoordinationLimits.MaxPayloadLength];
        Array.Fill(expected, (byte)'a');
        var prefix = "1\n"u8.ToArray();
        byte[] message = [.. prefix, .. expected];

        var found = RespNotificationService.TryReadPayload(message, out var payload);

        Assert.True(found);
        Assert.Equal(expected, payload);
    }

    [Fact]
    public void TryReadPayload_WhenThePayloadIsOverTheLimit_ReturnsFalse()
    {
        var oversized = new byte[CoordinationLimits.MaxPayloadLength + 1];
        Array.Fill(oversized, (byte)'a');
        var prefix = "1\n"u8.ToArray();
        byte[] message = [.. prefix, .. oversized];

        var found = RespNotificationService.TryReadPayload(message, out var payload);

        Assert.False(found);
        Assert.Empty(payload);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_Always_RunsTheScriptOnTheTopicsKeyAndChannel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object)
            .Verifiable();
        var (service, _) = CreateService(multiplexer.Object, RespProfiles.Garnet);
        var topic = CreateKeyspace().CreateKey("t");
        var key = RespKeys.ForKey(topic, RespNotificationService.Kind);
        var name = key.ToString();
        var payload = "pay"u8.ToArray();
        var text = RespScript.NotificationPublish.Text;
        var reply = RedisResult.Create((RedisValue)4, ResultType.Integer);
        RedisKey[]? capturedKeys = null;
        RedisValue[]? capturedValues = null;
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    text,
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    CommandFlags.None
                )
            )
            .Callback<string, RedisKey[]?, RedisValue[]?, CommandFlags>(
                (_, keys, values, _) =>
                {
                    capturedKeys = keys;
                    capturedValues = values;
                }
            )
            .ReturnsAsync(reply)
            .Verifiable();

        await service.PublishAsync(topic, payload, null, cancellationToken);

        Assert.NotNull(capturedKeys);
        Assert.NotNull(capturedValues);
        Assert.Equal<RedisKey>([key], capturedKeys);
        Assert.Equal<RedisValue>([name, "0", payload], capturedValues);
    }

    #endregion

    #region SubscribeAsync Tests

    [RespServerFact]
    public Task SubscribeAsync_Valkey_WhenTheSequenceMovesWithoutAMessage_ResyncsAtACheck() =>
        AssertSequenceMovingWithoutAMessageResyncsAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task SubscribeAsync_Garnet_WhenTheSequenceMovesWithoutAMessage_ResyncsAtACheck() =>
        AssertSequenceMovingWithoutAMessageResyncsAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task SubscribeAsync_Valkey_WhenAMessageSkipsANumber_Resyncs() =>
        AssertMessageSkippingANumberResyncsAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task SubscribeAsync_Garnet_WhenAMessageSkipsANumber_Resyncs() =>
        AssertMessageSkippingANumberResyncsAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task SubscribeAsync_Valkey_WhenAMessageHasNoPayload_ResyncsAtACheck() =>
        AssertMessageWithoutAPayloadResyncsAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task SubscribeAsync_Garnet_WhenAMessageHasNoPayload_ResyncsAtACheck() =>
        AssertMessageWithoutAPayloadResyncsAsync(servers.Garnet, RespProfiles.Garnet);

    [RespServerFact]
    public Task SubscribeAsync_Valkey_WhenAnotherSubscriptionEnds_KeepsDelivering() =>
        AssertAnotherSubscriptionEndingKeepsDeliveringAsync(servers.Valkey, RespProfiles.Valkey);

    [RespServerFact]
    public Task SubscribeAsync_Garnet_WhenAnotherSubscriptionEnds_KeepsDelivering() =>
        AssertAnotherSubscriptionEndingKeepsDeliveringAsync(servers.Garnet, RespProfiles.Garnet);

    #endregion
}
