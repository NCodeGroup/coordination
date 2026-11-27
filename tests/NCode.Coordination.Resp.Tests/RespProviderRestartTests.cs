// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
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
            await halfway.Task.WaitAsync(RespTestSupport.WaitLimit, cancellationToken);
            await server.StopAsync(cancellationToken);
            stopped.TrySetResult();
            await failed.Task.WaitAsync(RespTestSupport.WaitLimit, cancellationToken);
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

                    await stopped.Task.WaitAsync(RespTestSupport.WaitLimit, cancellationToken);
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

        var beforePayload = "before"u8.ToArray();
        await notifications.PublishAsync(topic, beforePayload, null, cancellationToken);
        var beforeKey = keyspace.CreateKey(RespTestSupport.WatchedName, "before");
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
