// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Tests.Servers;
using Npgsql;

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
    /// The name of the record that the tests write after the failover, through the new primary.
    /// </summary>
    private const string AfterName = "after";

    /// <summary>
    /// The services, keys and enumerators that a test publishes through and reads the changes back from.
    /// </summary>
    /// <param name="Notifications">The notification service to publish through.</param>
    /// <param name="Records">The record service to write through.</param>
    /// <param name="Keyspace">The keyspace that holds the watched records.</param>
    /// <param name="Topic">The topic that the messages are published to.</param>
    /// <param name="Messages">The enumerator of the subscription to the topic.</param>
    /// <param name="Changes">The enumerator of the watch on the records.</param>
    private sealed record PgFailoverStreams(
        INotificationService Notifications,
        IRecordService Records,
        Keyspace Keyspace,
        CoordinationKey Topic,
        IAsyncEnumerator<Notification> Messages,
        IAsyncEnumerator<RecordChange> Changes
    );

    /// <summary>
    /// Publishes numbered messages and writes numbered records, and fails the test when the subscription and the watch
    /// don't deliver each of them, in order.
    /// </summary>
    /// <param name="streams">The services, keys and enumerators to publish through and read back from.</param>
    /// <param name="name">The prefix of the payloads and the name of the records.</param>
    /// <param name="count">The number of messages and records to write.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when every message and change has been delivered.</returns>
    private static async ValueTask PublishAndAssertAsync(
        PgFailoverStreams streams,
        string name,
        int count,
        CancellationToken cancellationToken
    )
    {
        var value = "v"u8.ToArray();
        for (var index = 0; index < count; index++)
        {
            var payload = Encoding.UTF8.GetBytes($"{name}{index}");
            await streams.Notifications.PublishAsync(
                streams.Topic,
                payload,
                null,
                cancellationToken
            );
            var key = streams.Keyspace.CreateKey(PgTestSupport.WatchedName, name, index);
            await streams.Records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );

            var message = await PgTestSupport.NextAsync(streams.Messages, cancellationToken);
            Assert.False(message.IsResync);
            Assert.Equal(NotificationKind.Message, message.Kind);
            var received = message.Payload.ToArray();
            Assert.Equal(payload, received);
            var change = await PgTestSupport.NextAsync(streams.Changes, cancellationToken);
            Assert.False(change.IsResync);
            Assert.Equal(RecordChangeKind.Written, change.Kind);
            Assert.Equal(key, change.Key);
        }
    }

    /// <summary>
    /// Polls the primary until a backend waits for a synchronous standby to confirm its commit.
    /// </summary>
    /// <param name="connectionString">The connection string of the primary.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when a commit waits for a standby.</returns>
    private static async ValueTask WaitForCommitWaitingAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var pollInterval = TimeSpan.FromMilliseconds(100);
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event = 'SyncRep'",
                connection
            );
            var waiting = await command.ExecuteScalarAsync(cancellationToken);
            if (waiting is > 0L)
                return;

            if (stopwatch.Elapsed > PgTestSupport.WaitLimit)
                Assert.Fail("No commit waited for the standby.");

            await Task.Delay(pollInterval, cancellationToken);
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
            var key = keyspace.CreateKey(BeforeName, index);
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
            var key = keyspace.CreateKey(LostName, index);
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

        var afterKey = keyspace.CreateKey(AfterName);
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

    [PgServerFact]
    public async Task CreateAsync_WhenASynchronousStandbyIsPromoted_LosesNoAcknowledgedWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(true, cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Synchronous,
            cancellationToken
        );
        var records = PgTestSupport.GetService<IRecordService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();

        var before = new List<CoordinationKey>();
        for (var index = 0; index < Records; index++)
        {
            var key = keyspace.CreateKey(BeforeName, index);
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

        var lostKey = keyspace.CreateKey(LostName);
        var pending = records
            .CreateAsync(lostKey, value, RecordLifetime.Persistent, null, cancellationToken)
            .AsTask();
        await WaitForCommitWaitingAsync(server.PrimaryAdmin, cancellationToken);

        await server.PromoteStandbyAsync(cancellationToken);
        await server.StopPrimaryAsync(cancellationToken);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await pending.WaitAsync(PgTestSupport.WaitLimit, cancellationToken)
        );
        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);

        foreach (var key in before)
        {
            await PgTestSupport.WaitUntilAnsweredAsync(
                async ct =>
                {
                    var stored = await records.ReadAsync(key, null, ct);
                    Assert.NotNull(stored);
                },
                cancellationToken
            );
        }
    }

    #endregion

    #region ReadAsync Tests

    [PgServerFact]
    public async Task ReadAsync_OnTheOldPrimaryAfterThePromotion_MissesAWriteOfTheNewPrimary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(true, cancellationToken);
        var schemaName = $"t_{Guid.NewGuid():N}";
        var schema = PgSchemaName.Create(schemaName);
        await using var oldPrimary = await PgTestSupport.CreateProviderAsync(
            server.PrimaryAdmin,
            PostgreSqlProfiles.Synchronous,
            schema,
            cancellationToken
        );
        var oldRecords = PgTestSupport.GetService<IRecordService>(oldPrimary);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();

        var beforeKey = keyspace.CreateKey(BeforeName);
        var before = await oldRecords.CreateAsync(
            beforeKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(before.IsWritten);

        await server.WaitForStandbyAsync(cancellationToken);
        await server.CutReplicationAsync(cancellationToken);
        await server.PromoteStandbyAsync(cancellationToken);

        await using var newPrimary = await PgTestSupport.CreateProviderAsync(
            server.StandbyAdmin,
            PostgreSqlProfiles.Asynchronous,
            schema,
            cancellationToken
        );
        var newRecords = PgTestSupport.GetService<IRecordService>(newPrimary);
        var afterKey = keyspace.CreateKey(AfterName);
        var after = await newRecords.CreateAsync(
            afterKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(after.IsWritten);

        var onNew = await newRecords.ReadAsync(afterKey, null, cancellationToken);
        Assert.NotNull(onNew);
        var onOld = await oldRecords.ReadAsync(afterKey, null, cancellationToken);
        Assert.Null(onOld);
        var beforeOnOld = await oldRecords.ReadAsync(beforeKey, null, cancellationToken);
        Assert.NotNull(beforeOnOld);
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

        var streams = new PgFailoverStreams(
            notifications,
            records,
            keyspace,
            topic,
            messageEnumerator,
            changeEnumerator
        );
        await PublishAndAssertAsync(streams, BeforeName, Records, cancellationToken);

        await server.WaitForStandbyAsync(cancellationToken);
        await server.CutReplicationAsync(cancellationToken);

        await PublishAndAssertAsync(streams, LostName, Lost, cancellationToken);

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
