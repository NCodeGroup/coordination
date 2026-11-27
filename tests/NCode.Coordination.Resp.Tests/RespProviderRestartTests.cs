// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Text;
using NCode.Coordination.Resp.Tests.Conformance;
using NCode.Coordination.Resp.Tests.Servers;

namespace NCode.Coordination.Resp.Tests;

/// <summary>
/// Proves that a RESP provider reports failures honestly and resubscribes when its server restarts, each test restarting
/// a server of its own.
/// </summary>
public sealed class RespProviderRestartTests
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
    /// The name of the record prefix that the tests watch.
    /// </summary>
    private const string WatchedName = "watched";

    /// <summary>
    /// The number of rounds that a test waits for a subscription to resync and deliver again after a restart.
    /// </summary>
    private const int Attempts = 20;

    /// <summary>
    /// The prefix of the payloads and record names that a test writes after the restart, numbered by attempt.
    /// </summary>
    private const string AfterPrefix = "after-";

    /// <summary>
    /// Gets how long a test waits for a restarted server to answer or for a restart step to happen.
    /// </summary>
    private static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Restarts a server while clients write records and checks that every failure is unavailable and that no write
    /// reported as unapplied is stored.
    /// </summary>
    /// <param name="image">The image of the server to restart.</param>
    /// <param name="command">The arguments of the server's command, empty to run the image's default.</param>
    /// <param name="profile">The profile of the provider that writes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertWritesAcrossARestartAsync(
        string image,
        IReadOnlyList<string> command,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        await using var server = await RespRestartableServer.StartAsync(
            image,
            command,
            cancellationToken
        );
        var target = await RespConformance.CreateTargetAsync(
            server.Endpoint,
            profile,
            cancellationToken
        );
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var records = RespTestSupport.GetService<IRecordService>(target.Provider);
        var keyspace = RespTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new ConcurrentQueue<(CoordinationKey Key, CoordinationException Failure)>();
        var reached = 0;

        async ValueTask RestartAsync()
        {
            await halfway.Task.WaitAsync(WaitLimit, cancellationToken);
            await server.StopAsync(cancellationToken);
            stopped.TrySetResult();
            await failed.Task.WaitAsync(WaitLimit, cancellationToken);
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

                    await stopped.Task.WaitAsync(WaitLimit, cancellationToken);
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

        var afterKey = keyspace.CreateKey("after");
        await RespTestSupport.WaitUntilAnsweredAsync(
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

    /// <summary>
    /// Restarts a server while a subscription and a record watch are open and checks that each resyncs and then
    /// delivers later messages and changes.
    /// </summary>
    /// <param name="image">The image of the server to restart.</param>
    /// <param name="command">The arguments of the server's command, empty to run the image's default.</param>
    /// <param name="profile">The profile of the provider that subscribes.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>A task that completes when the checks pass.</returns>
    private static async ValueTask AssertSubscriptionsAcrossARestartAsync(
        string image,
        IReadOnlyList<string> command,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        await using var server = await RespRestartableServer.StartAsync(
            image,
            command,
            cancellationToken
        );
        var target = await RespConformance.CreateTargetAsync(
            server.Endpoint,
            profile,
            cancellationToken
        );
        await using var disposal = Assert.IsType<RespProvider>(target.Provider);
        var notifications = RespTestSupport.GetService<INotificationService>(target.Provider);
        var records = RespTestSupport.GetService<IRecordService>(target.Provider);
        var watches = RespTestSupport.GetService<IRecordWatchService>(target.Provider);
        var keyspace = RespTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();
        var topic = keyspace.CreateKey("topic");
        var prefix = keyspace.CreateKey(WatchedName);

        var messages = notifications.SubscribeAsync(topic, null, cancellationToken);
        await using var messageEnumerator = messages.GetAsyncEnumerator(cancellationToken);
        var changes = watches.WatchPrefixAsync(prefix, null, null, cancellationToken);
        await using var changeEnumerator = changes.GetAsyncEnumerator(cancellationToken);

        var messageStart = await RespTestSupport.NextAsync(messageEnumerator, cancellationToken);
        Assert.True(messageStart.IsResync);
        var changeStart = await RespTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.True(changeStart.IsResync);

        var beforePayload = "before"u8.ToArray();
        await notifications.PublishAsync(topic, beforePayload, null, cancellationToken);
        var beforeKey = keyspace.CreateKey(WatchedName, "before");
        await records.CreateAsync(
            beforeKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        var beforeMessage = await RespTestSupport.NextAsync(messageEnumerator, cancellationToken);
        Assert.Equal(NotificationKind.Message, beforeMessage.Kind);
        var beforeReceived = beforeMessage.Payload.ToArray();
        Assert.Equal(beforePayload, beforeReceived);
        var beforeChange = await RespTestSupport.NextAsync(changeEnumerator, cancellationToken);
        Assert.Equal(RecordChangeKind.Written, beforeChange.Kind);
        Assert.Equal(beforeKey, beforeChange.Key);

        await server.StopAsync(cancellationToken);
        await server.StartAgainAsync(cancellationToken);

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
            else
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
            else if (change.Kind == RecordChangeKind.Written)
                delivered = true;
        }

        Assert.True(resynced);
        Assert.True(delivered);
    }

    #region CreateAsync Tests

    [RespServerFact]
    public async Task CreateAsync_WhenValkeyRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied() =>
        await AssertWritesAcrossARestartAsync(
            RespServers.ValkeyImage,
            [],
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task CreateAsync_WhenGarnetRestartsMidRun_FailsOnlyAsUnavailableAndAppliesNoCallReportedUnapplied() =>
        await AssertWritesAcrossARestartAsync(
            RespServers.GarnetImage,
            RespServers.GarnetCommand,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

    #endregion

    #region SubscribeAsync Tests

    [RespServerFact]
    public async Task SubscribeAsync_WhenValkeyRestarts_ResyncsAndDeliversLaterChanges() =>
        await AssertSubscriptionsAcrossARestartAsync(
            RespServers.ValkeyImage,
            [],
            RespProfiles.Valkey,
            TestContext.Current.CancellationToken
        );

    [RespServerFact]
    public async Task SubscribeAsync_WhenGarnetRestarts_ResyncsAndDeliversLaterChanges() =>
        await AssertSubscriptionsAcrossARestartAsync(
            RespServers.GarnetImage,
            RespServers.GarnetCommand,
            RespProfiles.Garnet,
            TestContext.Current.CancellationToken
        );

    #endregion
}
