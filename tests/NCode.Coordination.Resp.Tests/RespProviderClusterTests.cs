// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Notifications;
using NCode.Coordination.Resp.Tests.Conformance;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests;

public sealed class RespProviderClusterTests(RespServers servers)
{
    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace("cluster", segment);
    }

    private static TService GetService<TService>(ICoordinationProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);

        return service;
    }

    private static async Task<T> NextAsync<T>(
        IAsyncEnumerator<T> enumerator,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(15);
        var moving = enumerator.MoveNextAsync().AsTask();
        var moved = await moving.WaitAsync(timeout, cancellationToken);
        Assert.True(moved);

        return enumerator.Current;
    }

    private static async Task<IAsyncEnumerator<Notification>> SubscribeAndPublishAsync(
        INotificationService notifications,
        CoordinationKey topic,
        CancellationToken cancellationToken
    )
    {
        var stream = notifications.SubscribeAsync(topic, null, cancellationToken);
        var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        var start = await NextAsync(enumerator, cancellationToken);
        Assert.True(start.IsResync);
        var payload = "p"u8.ToArray();
        await notifications.PublishAsync(topic, payload, null, cancellationToken);
        var message = await NextAsync(enumerator, cancellationToken);
        Assert.Equal(NotificationKind.Message, message.Kind);
        var received = message.Payload.ToArray();
        Assert.Equal(payload, received);

        return enumerator;
    }

    private static async Task<List<int>> FindShardChannelAsync(
        IServer[] clusterServers,
        string channel
    )
    {
        var holders = new List<int>();

        for (var index = 0; index < clusterServers.Length; index++)
        {
            var result = await clusterServers[index].ExecuteAsync("PUBSUB", "SHARDCHANNELS");
            var channels = (string[]?)result;
            Assert.NotNull(channels);

            if (channels.Contains(channel))
                holders.Add(index);
        }

        return holders;
    }

    private async Task<ConnectionMultiplexer> ConnectAdminAsync(CancellationToken cancellationToken)
    {
        var options = ConfigurationOptions.Parse(servers.ValkeyCluster);
        options.AllowAdmin = true;
        var connecting = ConnectionMultiplexer.ConnectAsync(options);

        return await connecting.WaitAsync(cancellationToken);
    }

    #region Operations Tests

    [RespServerFact]
    public async Task Operations_WhenEveryKindOfStateIsWritten_KeepEveryKeyOfTheKeyspaceInItsSlot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await RespConformance.CreateTargetAsync(
            servers.ValkeyCluster,
            RespProfiles.Valkey,
            cancellationToken
        );
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var provider = target.Provider;
        var keyspace = CreateKeyspace();
        var lifetime = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(lifetime);

        var records = GetService<IRecordService>(provider);
        var sessions = GetService<ISessionService>(provider);
        var counters = GetService<ICounterService>(provider);
        var occurrences = GetService<IOccurrenceService>(provider);
        var sets = GetService<ISetService>(provider);
        var sequences = GetService<ISequenceService>(provider);
        var notifications = GetService<INotificationService>(provider);
        var recordKey = keyspace.CreateKey("record");
        var counterKey = keyspace.CreateKey("counter");
        var occurrenceKey = keyspace.CreateKey("occurrence");
        var setKey = keyspace.CreateKey("set");
        var sequenceKey = keyspace.CreateKey("sequence");
        var topic = keyspace.CreateKey("topic");
        var value = "v"u8.ToArray();
        var payload = "p"u8.ToArray();
        await records.CreateAsync(
            recordKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        await sessions.OpenAsync(keyspace, timeToLive, null, cancellationToken);
        await counters.IncrementAsync(counterKey, 1, timeToLive, null, cancellationToken);
        await occurrences.TryRecordAsync(occurrenceKey, timeToLive, null, cancellationToken);
        await sets.AddAsync(setKey, "m", timeToLive, null, cancellationToken);
        await sequences.NextAsync(sequenceKey, null, cancellationToken);
        await notifications.PublishAsync(topic, payload, null, cancellationToken);

        await using var admin = await ConnectAdminAsync(cancellationToken);
        var clusterServers = admin.GetServers();
        var tag = RespKeys.KeyspaceTag(keyspace);
        var expectedSlot = admin.HashSlot(tag);
        var holders = new List<List<RedisKey>>();
        foreach (var server in clusterServers)
        {
            var keys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: tag + "*"))
                keys.Add(key);

            if (keys.Count > 0)
                holders.Add(keys);
        }

        Assert.Equal(3, clusterServers.Length);
        var holder = Assert.Single(holders);
        Assert.True(holder.Count >= 7);
        foreach (var key in holder)
        {
            var slot = admin.HashSlot(key);
            Assert.Equal(expectedSlot, slot);
        }
    }

    #endregion

    #region SubscribeAsync Tests

    [RespServerFact]
    public async Task SubscribeAsync_WhenKeyspacesLiveOnDifferentNodes_SubscribesEachOnItsSlotsNode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await RespConformance.CreateTargetAsync(
            servers.ValkeyCluster,
            RespProfiles.Valkey,
            cancellationToken
        );
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var notifications = GetService<INotificationService>(target.Provider);
        await using var admin = await ConnectAdminAsync(cancellationToken);
        var clusterServers = admin.GetServers();
        var configuration = clusterServers[0].ClusterConfiguration;
        Assert.NotNull(configuration);

        var first = CreateKeyspace();
        var firstTag = RespKeys.KeyspaceTag(first);
        var firstSlot = admin.HashSlot(firstTag);
        var firstNode = configuration.GetBySlot(firstSlot)?.NodeId;
        Assert.NotNull(firstNode);
        Keyspace? second = null;
        for (var attempt = 0; attempt < 64 && second is null; attempt++)
        {
            var candidate = CreateKeyspace();
            var candidateTag = RespKeys.KeyspaceTag(candidate);
            var slot = admin.HashSlot(candidateTag);
            if (configuration.GetBySlot(slot)?.NodeId != firstNode)
                second = candidate;
        }

        Assert.NotNull(second);
        var firstTopic = first.CreateKey("topic");
        var secondTopic = second.CreateKey("topic");
        await using var firstEnumerator = await SubscribeAndPublishAsync(
            notifications,
            firstTopic,
            cancellationToken
        );
        await using var secondEnumerator = await SubscribeAndPublishAsync(
            notifications,
            secondTopic,
            cancellationToken
        );

        var firstChannel = RespKeys.ForKey(firstTopic, RespNotificationService.Kind).ToString();
        var secondChannel = RespKeys.ForKey(secondTopic, RespNotificationService.Kind).ToString();
        var firstHolders = await FindShardChannelAsync(clusterServers, firstChannel);
        var secondHolders = await FindShardChannelAsync(clusterServers, secondChannel);

        var firstHolder = Assert.Single(firstHolders);
        var secondHolder = Assert.Single(secondHolders);
        Assert.NotEqual(firstHolder, secondHolder);
    }

    #endregion
}
