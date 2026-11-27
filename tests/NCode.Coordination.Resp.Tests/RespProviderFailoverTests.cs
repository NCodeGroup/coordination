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
    /// The name of the keyspaces that the tests create, which tells their keys apart from other tests'.
    /// </summary>
    private const string KeyspaceName = "failover";

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
        var keyspace = RespTestSupport.CreateKeyspace(KeyspaceName);
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
        var keyspace = RespTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();
        var topic = keyspace.CreateKey(RespTestSupport.TopicName);
        var prefix = keyspace.CreateKey(RespTestSupport.WatchedName);

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
        var lostKey = keyspace.CreateKey(RespTestSupport.WatchedName, LostName);
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

        await RespTestSupport.AssertTopicResyncsThenDeliversAsync(
            notifications,
            topic,
            messageEnumerator,
            cancellationToken
        );
        await RespTestSupport.AssertWatchResyncsThenDeliversAsync(
            records,
            keyspace,
            changeEnumerator,
            cancellationToken
        );
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
