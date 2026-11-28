// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Text;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Tests;

/// <summary>
/// Proves that a PostgreSQL provider reports failures honestly when its server restarts, each test restarting a server
/// of its own.
/// </summary>
public sealed class PostgreSqlProviderRestartTests
{
    /// <summary>
    /// The name of the keyspaces that the tests create, which tells their keys apart from other tests'.
    /// </summary>
    private const string KeyspaceName = "restart";

    /// <summary>
    /// The number of clients that write concurrently while the server restarts.
    /// </summary>
    private const int Clients = 3;

    /// <summary>
    /// The number of records that each client writes.
    /// </summary>
    private const int Steps = 12;

    /// <summary>
    /// The payload of the message, and the name of the record, that the tests write before the restart.
    /// </summary>
    private const string BeforeName = "before";

    /// <summary>
    /// The name of the record that the tests write after the restart.
    /// </summary>
    private const string AfterName = "after";

    #region CreateAsync Tests

    [PgServerFact]
    public async Task CreateAsync_WhenTheServerRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgRestartableServer.StartAsync(cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Standalone,
            cancellationToken
        );
        var records = PgTestSupport.GetService<IRecordService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new ConcurrentQueue<(CoordinationKey Key, CoordinationException Failure)>();
        var reached = 0;

        async ValueTask RestartAsync()
        {
            await halfway.Task.WaitAsync(PgTestSupport.WaitLimit, cancellationToken);
            await server.StopAsync(cancellationToken);
            stopped.TrySetResult();
            await failed.Task.WaitAsync(PgTestSupport.WaitLimit, cancellationToken);
            await server.StartAgainAsync(cancellationToken);
        }

        async ValueTask ClientAsync(int client)
        {
            for (var step = 0; step < Steps; step++)
            {
                if (step == Steps / 2)
                {
                    if (Interlocked.Increment(ref reached) == Clients)
                        halfway.TrySetResult();

                    await stopped.Task.WaitAsync(PgTestSupport.WaitLimit, cancellationToken);
                }

                var key = keyspace.CreateKey("w", client, step);
                try
                {
                    await records.CreateAsync(
                        key,
                        value,
                        RecordLifetime.Persistent,
                        null,
                        cancellationToken
                    );
                }
                catch (CoordinationException exception)
                {
                    failures.Enqueue((key, exception));
                    failed.TrySetResult();
                }
            }
        }

        var restarting = RestartAsync().AsTask();
        List<Task> running = [restarting];
        for (var client = 0; client < Clients; client++)
        {
            var writer = ClientAsync(client).AsTask();
            running.Add(writer);
        }

        await Task.WhenAll(running);

        var afterKey = keyspace.CreateKey(AfterName);
        await PgTestSupport.WaitUntilAnsweredAsync(
            async ct =>
            {
                await records.CreateAsync(afterKey, value, RecordLifetime.Persistent, null, ct);
            },
            cancellationToken
        );

        Assert.NotEmpty(failures);
        foreach (var (key, failure) in failures)
        {
            Assert.Equal(CoordinationErrorCodes.Unavailable, failure.Code);
            if (!failure.OutcomeUnknown)
            {
                var stored = await records.ReadAsync(key, null, cancellationToken);
                Assert.Null(stored);
            }
        }

        var after = await records.ReadAsync(afterKey, null, cancellationToken);
        Assert.NotNull(after);
    }

    #endregion

    #region SubscribeAsync Tests

    [PgServerFact]
    public async Task SubscribeAsync_WhenTheServerRestarts_ResyncsAndDeliversLaterChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgRestartableServer.StartAsync(cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Standalone,
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

        var beforePayload = Encoding.UTF8.GetBytes(BeforeName);
        await notifications.PublishAsync(topic, beforePayload, null, cancellationToken);
        var beforeKey = keyspace.CreateKey(PgTestSupport.WatchedName, BeforeName);
        await records.CreateAsync(
            beforeKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        var beforeMessage = await PgTestSupport.NextAsync(messageEnumerator, cancellationToken);
        Assert.Equal(NotificationKind.Message, beforeMessage.Kind);
        var beforeReceived = beforeMessage.Payload.ToArray();
        Assert.Equal(beforePayload, beforeReceived);
        var beforeChange = await PgTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.Equal(RecordChangeKind.Written, beforeChange.Kind);
        Assert.Equal(beforeKey, beforeChange.Key);

        await server.StopAsync(cancellationToken);
        await server.StartAgainAsync(cancellationToken);

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
    public async Task WatchKeyAsync_AfterARevisionFromBeforeTheRestart_StartsWithAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgRestartableServer.StartAsync(cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Standalone,
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

        await server.StopAsync(cancellationToken);
        await server.StartAgainAsync(cancellationToken);

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
