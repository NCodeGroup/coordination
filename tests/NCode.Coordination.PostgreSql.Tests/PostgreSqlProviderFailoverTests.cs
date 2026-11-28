// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Tests;

/// <summary>
/// Proves what a PostgreSQL provider keeps and loses when a standby is promoted, each test failing over servers of its
/// own.
/// </summary>
public sealed class PostgreSqlProviderFailoverTests
{
    /// <summary>
    /// The name of the keyspaces that the tests create, which tells their keys apart from other tests'.
    /// </summary>
    private const string KeyspaceName = "failover";

    /// <summary>
    /// The number of records that the tests write at each step.
    /// </summary>
    private const int Records = 5;

    /// <summary>
    /// The number of messages and writes that the old primary takes after replication is cut, which the promoted
    /// server never sees.
    /// </summary>
    private const int Lost = 3;

    /// <summary>
    /// The name of the record, and the prefix of the payload of the message, that the tests write before the failover.
    /// </summary>
    private const string BeforeName = "before";

    /// <summary>
    /// The name of the record, and the prefix of the payload of the message, that the old primary takes after
    /// replication is cut.
    /// </summary>
    private const string LostName = "lost";

    /// <summary>
    /// Moves an enumerator to its next item that is not a resync, so that a periodic check between two writes doesn't
    /// fail a test that counts deliveries.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="enumerator">The enumerator to move.</param>
    /// <param name="isResync">The function that tells whether an item is a resync.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The next item that is not a resync.</returns>
    private static async ValueTask<T> NextDeliveryAsync<T>(
        IAsyncEnumerator<T> enumerator,
        Func<T, bool> isResync,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var item = await PgTestSupport.NextAsync(enumerator, cancellationToken);
            if (!isResync(item))
                return item;
        }
    }

    #region ProbeAsync Tests

    [PgServerFact]
    public async Task ProbeAsync_Standby_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(false, cancellationToken);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
        {
            await using var provider = await PgTestSupport.CreateProviderAsync(
                server.StandbyAdmin,
                PostgreSqlProfiles.Asynchronous,
                cancellationToken
            );
        });

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region CreateAsync Tests

    [PgServerFact]
    public async Task CreateAsync_WhenTheStandbyIsPromotedAfterReplicationIsCut_LosesOnlyTheWritesAfterTheCut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(false, cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Asynchronous,
            cancellationToken
        );
        var records = PgTestSupport.GetService<IRecordService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();

        var before = new List<CoordinationKey>();
        for (var index = 0; index < Records; index++)
        {
            var key = keyspace.CreateKey("before", index);
            var created = await records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );
            Assert.True(created.IsWritten);
            before.Add(key);
        }

        await server.WaitForStandbyAsync(cancellationToken);
        await server.CutReplicationAsync(cancellationToken);

        var lost = new List<CoordinationKey>();
        for (var index = 0; index < Records; index++)
        {
            var key = keyspace.CreateKey("lost", index);
            var created = await records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );
            Assert.True(created.IsWritten);
            lost.Add(key);
        }

        await server.PromoteStandbyAsync(cancellationToken);
        await server.StopPrimaryAsync(cancellationToken);

        var afterKey = keyspace.CreateKey("after");
        await PgTestSupport.WaitUntilAnsweredAsync(
            async ct =>
            {
                await records.CreateAsync(afterKey, value, RecordLifetime.Persistent, null, ct);
            },
            cancellationToken
        );

        foreach (var key in before)
        {
            var stored = await records.ReadAsync(key, null, cancellationToken);
            Assert.NotNull(stored);
        }

        foreach (var key in lost)
        {
            var stored = await records.ReadAsync(key, null, cancellationToken);
            Assert.Null(stored);
        }

        var after = await records.ReadAsync(afterKey, null, cancellationToken);
        Assert.NotNull(after);
    }

    #endregion

    #region SubscribeAsync Tests

    [PgServerFact]
    public async Task SubscribeAsync_WhenTheStandbyIsPromotedAndTakesWritesPastTheOldNumbers_Resyncs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(false, cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Asynchronous,
            cancellationToken
        );
        var notifications = PgTestSupport.GetService<INotificationService>(provider);
        var records = PgTestSupport.GetService<IRecordService>(provider);
        var watches = PgTestSupport.GetService<IRecordWatchService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();
        var topic = keyspace.CreateKey(PgTestSupport.TopicName);
        var prefix = keyspace.CreateKey(PgTestSupport.WatchedName);

        var messages = notifications.SubscribeAsync(topic, null, cancellationToken);
        await using var messageEnumerator = messages.GetAsyncEnumerator(cancellationToken);
        var changes = watches.WatchPrefixAsync(prefix, null, null, cancellationToken);
        await using var changeEnumerator = changes.GetAsyncEnumerator(cancellationToken);

        var messageStart = await PgTestSupport.NextAsync(messageEnumerator, cancellationToken);
        Assert.True(messageStart.IsResync);
        var changeStart = await PgTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.True(changeStart.IsResync);

        for (var index = 0; index < Records; index++)
        {
            var payload = Encoding.UTF8.GetBytes($"{BeforeName}{index}");
            await notifications.PublishAsync(topic, payload, null, cancellationToken);
            var key = keyspace.CreateKey(PgTestSupport.WatchedName, BeforeName, index);
            await records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );

            var message = await NextDeliveryAsync(
                messageEnumerator,
                notification => notification.IsResync,
                cancellationToken
            );
            Assert.Equal(NotificationKind.Message, message.Kind);
            var received = message.Payload.ToArray();
            Assert.Equal(payload, received);
            var change = await NextDeliveryAsync(
                changeEnumerator,
                recordChange => recordChange.IsResync,
                cancellationToken
            );
            Assert.Equal(RecordChangeKind.Written, change.Kind);
            Assert.Equal(key, change.Key);
        }

        await server.WaitForStandbyAsync(cancellationToken);
        await server.CutReplicationAsync(cancellationToken);

        for (var index = 0; index < Lost; index++)
        {
            var payload = Encoding.UTF8.GetBytes($"{LostName}{index}");
            await notifications.PublishAsync(topic, payload, null, cancellationToken);
            var key = keyspace.CreateKey(PgTestSupport.WatchedName, LostName, index);
            await records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );

            var message = await NextDeliveryAsync(
                messageEnumerator,
                notification => notification.IsResync,
                cancellationToken
            );
            Assert.Equal(NotificationKind.Message, message.Kind);
            var received = message.Payload.ToArray();
            Assert.Equal(payload, received);
            var change = await NextDeliveryAsync(
                changeEnumerator,
                recordChange => recordChange.IsResync,
                cancellationToken
            );
            Assert.Equal(RecordChangeKind.Written, change.Kind);
            Assert.Equal(key, change.Key);
        }

        await server.PromoteStandbyAsync(cancellationToken);
        await server.StopPrimaryAsync(cancellationToken);

        await PgTestSupport.AssertTopicResyncsThenDeliversAsync(
            notifications,
            topic,
            messageEnumerator,
            cancellationToken
        );
        await PgTestSupport.AssertWatchResyncsThenDeliversAsync(
            records,
            keyspace,
            changeEnumerator,
            cancellationToken
        );
    }

    #endregion

    #region WatchKeyAsync Tests

    [PgServerFact]
    public async Task WatchKeyAsync_AfterARevisionFromBeforeTheFailover_StartsWithAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(false, cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Asynchronous,
            cancellationToken
        );
        var records = PgTestSupport.GetService<IRecordService>(provider);
        var watches = PgTestSupport.GetService<IRecordWatchService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();
        var prefix = keyspace.CreateKey(PgTestSupport.WatchedName);

        var changes = watches.WatchPrefixAsync(prefix, null, null, cancellationToken);
        await using var changeEnumerator = changes.GetAsyncEnumerator(cancellationToken);
        var changeStart = await PgTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.True(changeStart.IsResync);

        var beforeKey = keyspace.CreateKey(PgTestSupport.WatchedName, BeforeName);
        await records.CreateAsync(
            beforeKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        var beforeChange = await PgTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.Equal(RecordChangeKind.Written, beforeChange.Kind);
        var earlier = beforeChange.Revision;

        await server.WaitForStandbyAsync(cancellationToken);
        await server.CutReplicationAsync(cancellationToken);
        await server.PromoteStandbyAsync(cancellationToken);
        await server.StopPrimaryAsync(cancellationToken);

        await PgTestSupport.AssertWatchResyncsThenDeliversAsync(
            records,
            keyspace,
            changeEnumerator,
            cancellationToken
        );

        var replay = watches.WatchKeyAsync(beforeKey, earlier, null, cancellationToken);
        await using var replayEnumerator = replay.GetAsyncEnumerator(cancellationToken);
        var first = await PgTestSupport.NextAsync(replayEnumerator, cancellationToken);
        Assert.True(first.IsResync);
    }

    #endregion
}
