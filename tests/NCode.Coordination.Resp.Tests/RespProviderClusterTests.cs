// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Notifications;
using NCode.Coordination.Resp.Tests.Conformance;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests;

/// <summary>
/// Proves that each keyspace of the RESP provider stays in one slot of the Valkey and Garnet clusters, and that its
/// channel reaches its subscribers there.
/// </summary>
public sealed class RespProviderClusterTests(RespServers servers)
{
    /// <summary>
    /// The name of the notification topic that the tests publish to and subscribe on.
    /// </summary>
    private const string TopicName = "topic";

    /// <summary>
    /// The most keyspaces a test creates while it looks for one on another node; each lands on a given node one time in
    /// three.
    /// </summary>
    private const int KeyspaceAttempts = 64;

    /// <summary>
    /// Creates a keyspace with a unique id, so that the keys of one test are its own and no other test's keys match
    /// their prefix.
    /// </summary>
    /// <returns>The keyspace.</returns>
    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);

        return new Keyspace("cluster", segment);
    }

    /// <summary>
    /// Gets a primitive service of the provider, failing the test when the provider offers none.
    /// </summary>
    /// <typeparam name="TService">The service interface to get.</typeparam>
    /// <param name="provider">The provider that offers the service.</param>
    /// <returns>The service.</returns>
    private static TService GetService<TService>(ICoordinationProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);

        return service;
    }

    /// <summary>
    /// Moves an enumerator to its next item, failing the test when none arrives in time.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="enumerator">The enumerator to move.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The next item.</returns>
    private static async ValueTask<T> NextAsync<T>(
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

    /// <summary>
    /// Subscribes to a topic, waits for the subscription's first resync, publishes a payload and checks that it
    /// arrives, so the topic's sharded channel exists on its node.
    /// </summary>
    /// <param name="notifications">The notification service to subscribe and publish through.</param>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="cancellationToken">The token that cancels the waits.</param>
    /// <returns>The enumerator of the subscription, which keeps the channel open until it is disposed.</returns>
    private static async ValueTask<IAsyncEnumerator<Notification>> SubscribeAndPublishAsync(
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

    /// <summary>
    /// Finds the nodes of the cluster on which a sharded channel exists.
    /// </summary>
    /// <param name="clusterServers">The nodes of the cluster.</param>
    /// <param name="channel">The name of the sharded channel.</param>
    /// <returns>The indexes in <paramref name="clusterServers"/> of the nodes that hold the channel.</returns>
    private static async ValueTask<IReadOnlyList<int>> FindShardChannelAsync(
        IReadOnlyList<IServer> clusterServers,
        string channel
    )
    {
        var holders = new List<int>();

        for (var index = 0; index < clusterServers.Count; index++)
        {
            var result = await clusterServers[index].ExecuteAsync("PUBSUB", "SHARDCHANNELS");
            var channels = (string[]?)result;
            Assert.NotNull(channels);

            if (channels.Contains(channel))
                holders.Add(index);
        }

        return holders;
    }

    /// <summary>
    /// Writes every kind of state in one keyspace through a provider on a cluster and checks that every key of the
    /// keyspace lies in the keyspace's slot on one node.
    /// </summary>
    /// <param name="endpoint">The <c>host:port</c> list of the cluster's nodes.</param>
    /// <param name="profile">The profile of the provider that writes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertKeysInTheirSlotAsync(
        string endpoint,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        var target = await RespConformance.CreateTargetAsync(endpoint, profile, cancellationToken);
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
        var topic = keyspace.CreateKey(TopicName);
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

        await using var admin = await RespNodes.ConnectAdminAsync(endpoint, cancellationToken);
        var clusterServers = admin.GetServers();
        var tag = RespKeys.KeyspaceTag(keyspace);
        var expectedSlot = admin.HashSlot(tag);
        var holders = new List<List<RedisKey>>();
        var pattern = tag + "*";
        foreach (var server in clusterServers)
        {
            var keys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: pattern))
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

    #region Operations Tests

    [RespServerFact]
    public async Task Operations_WhenEveryKindOfStateIsWrittenToValkey_KeepEveryKeyOfTheKeyspaceInItsSlot() =>
        await AssertKeysInTheirSlotAsync(
            servers.ValkeyCluster,
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task Operations_WhenEveryKindOfStateIsWrittenToGarnet_KeepEveryKeyOfTheKeyspaceInItsSlot() =>
        await AssertKeysInTheirSlotAsync(
            servers.GarnetCluster,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

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
        await using var admin = await RespNodes.ConnectAdminAsync(
            servers.ValkeyCluster,
            cancellationToken
        );
        var clusterServers = admin.GetServers();
        var configuration = clusterServers[0].ClusterConfiguration;
        Assert.NotNull(configuration);

        var first = CreateKeyspace();
        var firstTag = RespKeys.KeyspaceTag(first);
        var firstSlot = admin.HashSlot(firstTag);
        var firstNode = configuration.GetBySlot(firstSlot)?.NodeId;
        Assert.NotNull(firstNode);
        Keyspace? second = null;
        for (var attempt = 0; attempt < KeyspaceAttempts && second is null; attempt++)
        {
            var candidate = CreateKeyspace();
            var candidateTag = RespKeys.KeyspaceTag(candidate);
            var slot = admin.HashSlot(candidateTag);
            if (configuration.GetBySlot(slot)?.NodeId != firstNode)
                second = candidate;
        }

        Assert.NotNull(second);
        var firstTopic = first.CreateKey(TopicName);
        var secondTopic = second.CreateKey(TopicName);
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
