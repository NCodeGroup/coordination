// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using NCode.Coordination.Resp.Tests.Conformance;
using NCode.Coordination.Resp.Tests.Servers;

namespace NCode.Coordination.Resp.Tests;

/// <summary>
/// Proves what a RESP provider keeps and loses when a replica is promoted while its primary still takes writes, each
/// test failing over servers of its own.
/// </summary>
public sealed class RespProviderFailoverTests
{
    /// <summary>
    /// The client option that has StackExchange.Redis check every server's role each second, so it finds a promoted
    /// Valkey replica with no Sentinel to announce it.
    /// </summary>
    private const string RoleCheckOption = ",configCheckSeconds=1";

    /// <summary>
    /// The name of the record, and the payload of the message, that the old primary takes after the promotion.
    /// </summary>
    private const string LostName = "lost";

    /// <summary>
    /// The name of the record prefix that the tests watch.
    /// </summary>
    private const string WatchedName = "watched";

    /// <summary>
    /// The name of the topic that the tests subscribe to.
    /// </summary>
    private const string TopicName = "topic";

    /// <summary>
    /// The number of rounds that a test waits for a subscription to resync and deliver again after a failover.
    /// </summary>
    private const int Attempts = 20;

    /// <summary>
    /// The prefix of the payloads and record names that a test writes after the failover, numbered by attempt.
    /// </summary>
    private const string AfterPrefix = "after-";

    /// <summary>
    /// Promotes a replica while the provider writes and checks that the write the old primary took after the promotion
    /// is the only one lost.
    /// </summary>
    /// <param name="start">The function that starts the primary and its replica.</param>
    /// <param name="profile">The profile of the provider that writes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertWritesAcrossAFailoverAsync(
        Func<CancellationToken, ValueTask<IRespFailoverServer>> start,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        await using var server = await start(cancellationToken);
        var endpoint = server.Endpoint + RoleCheckOption;
        var target = await RespConformance.CreateTargetAsync(endpoint, profile, cancellationToken);
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var records = RespTestSupport.GetService<IRecordService>(target.Provider);
        var keyspace = RespTestSupport.CreateKeyspace("failover");
        var value = "v"u8.ToArray();
        var beforeKey = keyspace.CreateKey("before");
        var lostKey = keyspace.CreateKey(LostName);
        var afterKey = keyspace.CreateKey("after");

        var before = await records.CreateAsync(
            beforeKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(before.IsWritten);
        await server.WaitForReplicaAsync(cancellationToken);
        await server.PromoteReplicaAsync(cancellationToken);
        var lost = await records.CreateAsync(
            lostKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(lost.IsWritten);
        await server.DemotePrimaryAsync(cancellationToken);
        await RespTestSupport.WaitUntilAnsweredAsync(
            async ct =>
                await records.CreateAsync(afterKey, value, RecordLifetime.Persistent, null, ct),
            cancellationToken
        );

        var beforeRead = await records.ReadAsync(beforeKey, null, cancellationToken);
        var lostRead = await records.ReadAsync(lostKey, null, cancellationToken);
        var afterRead = await records.ReadAsync(afterKey, null, cancellationToken);
        Assert.NotNull(beforeRead);
        Assert.Null(lostRead);
        Assert.NotNull(afterRead);
    }

    /// <summary>
    /// Promotes a replica while a subscription and a record watch read what the old primary takes, and checks that each
    /// resyncs when the failover takes its numbers back and then delivers later messages and changes.
    /// </summary>
    /// <param name="start">The function that starts the primary and its replica.</param>
    /// <param name="profile">The profile of the provider that subscribes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertSubscriptionsAcrossAFailoverAsync(
        Func<CancellationToken, ValueTask<IRespFailoverServer>> start,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        await using var server = await start(cancellationToken);
        var endpoint = server.Endpoint + RoleCheckOption;
        var target = await RespConformance.CreateTargetAsync(endpoint, profile, cancellationToken);
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var notifications = RespTestSupport.GetService<INotificationService>(target.Provider);
        var records = RespTestSupport.GetService<IRecordService>(target.Provider);
        var watches = RespTestSupport.GetService<IRecordWatchService>(target.Provider);
        var keyspace = RespTestSupport.CreateKeyspace("failover");
        var value = "v"u8.ToArray();
        var topic = keyspace.CreateKey(TopicName);
        var prefix = keyspace.CreateKey(WatchedName);

        var messages = notifications.SubscribeAsync(topic, null, cancellationToken);
        await using var messageEnumerator = messages.GetAsyncEnumerator(cancellationToken);
        var changes = watches.WatchPrefixAsync(prefix, null, null, cancellationToken);
        await using var changeEnumerator = changes.GetAsyncEnumerator(cancellationToken);

        var messageStart = await RespTestSupport.NextAsync(messageEnumerator, cancellationToken);
        Assert.True(messageStart.IsResync);
        var changeStart = await RespTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.True(changeStart.IsResync);

        await server.WaitForReplicaAsync(cancellationToken);
        await server.PromoteReplicaAsync(cancellationToken);
        var lostPayload = Encoding.UTF8.GetBytes(LostName);
        await notifications.PublishAsync(topic, lostPayload, null, cancellationToken);
        var lostKey = keyspace.CreateKey(WatchedName, LostName);
        await records.CreateAsync(
            lostKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        var lostMessage = await RespTestSupport.NextAsync(messageEnumerator, cancellationToken);
        Assert.Equal(NotificationKind.Message, lostMessage.Kind);
        var lostChange = await RespTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.Equal(RecordChangeKind.Written, lostChange.Kind);
        await server.DemotePrimaryAsync(cancellationToken);

        var resynced = false;
        var delivered = false;
        for (var attempt = 0; attempt < Attempts && !(resynced && delivered); attempt++)
        {
            var payload = Encoding.UTF8.GetBytes($"{AfterPrefix}{attempt}");
            await RespTestSupport.WaitUntilAnsweredAsync(
                ct => notifications.PublishAsync(topic, payload, null, ct),
                cancellationToken
            );

            var notification = await RespTestSupport.NextAsync(
                messageEnumerator,
                cancellationToken
            );
            if (notification.IsResync)
                resynced = true;
            else if (resynced)
                delivered = true;
        }

        Assert.True(resynced);
        Assert.True(delivered);

        resynced = false;
        delivered = false;
        for (var attempt = 0; attempt < Attempts && !(resynced && delivered); attempt++)
        {
            var key = keyspace.CreateKey(WatchedName, $"{AfterPrefix}{attempt}");
            await RespTestSupport.WaitUntilAnsweredAsync(
                async ct =>
                {
                    await records.CreateAsync(key, value, RecordLifetime.Persistent, null, ct);
                },
                cancellationToken
            );

            var change = await RespTestSupport.NextAsync(changeEnumerator, cancellationToken);
            if (change.IsResync)
                resynced = true;
            else if (resynced && change.Kind == RecordChangeKind.Written)
                delivered = true;
        }

        Assert.True(resynced);
        Assert.True(delivered);
    }

    #region CreateAsync Tests

    [RespServerFact]
    public async Task CreateAsync_WhenValkeyFailsOverWhileThePrimaryTakesWrites_LosesOnlyTheWritesAfterThePromotion() =>
        await AssertWritesAcrossAFailoverAsync(
            RespValkeyFailoverServer.StartAsync,
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task CreateAsync_WhenGarnetFailsOverWhileThePrimaryTakesWrites_LosesOnlyTheWritesAfterThePromotion() =>
        await AssertWritesAcrossAFailoverAsync(
            RespGarnetFailoverServer.StartAsync,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

    #endregion

    #region SubscribeAsync Tests

    [RespServerFact]
    public async Task SubscribeAsync_WhenValkeyFailsOverAndNumbersGoBack_ResyncsAndDeliversLaterChanges() =>
        await AssertSubscriptionsAcrossAFailoverAsync(
            RespValkeyFailoverServer.StartAsync,
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task SubscribeAsync_WhenGarnetFailsOverAndNumbersGoBack_ResyncsAndDeliversLaterChanges() =>
        await AssertSubscriptionsAcrossAFailoverAsync(
            RespGarnetFailoverServer.StartAsync,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

    #endregion
}
